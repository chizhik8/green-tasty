import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  defer,
  exhaustMap,
  finalize,
  forkJoin,
  interval,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

const SYNC_INTERVAL_MS = 15_000;

const STATUS_LABELS: Record<GroupReservationStatus, string> = {
  Open: 'Collecting pre-orders',
  Submitted: 'Sent to Restaurant',
  Closed: 'Closed',
};

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ModalComponent, ParticipantCardComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly groupOrdersService = inject(GroupOrdersService);
  private readonly toastService = inject(ToastService);

  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId');
  private syncSubscription: Subscription | null = null;
  /** Bumped when a submit or remove starts; a refresh that started earlier is dropped. */
  private mutationCount = 0;
  private refreshing = false;

  readonly viewState = signal<'loading' | 'ready' | 'not-found' | 'inactive' | 'error'>('loading');
  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal<string | null>(null);
  readonly status = signal<GroupReservationStatus | null>(null);
  readonly submitting = signal(false);
  readonly submitDialogOpen = signal(false);
  readonly lastUpdated = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly pendingRemoval = signal<Participant | null>(null);
  readonly removing = signal(false);

  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  readonly grandTotal = computed(() => this.consolidated().total);
  readonly consolidatedRows = computed(() =>
    this.consolidated().items.map((item) => ({
      id: item.dish.id,
      label: `${item.dish.name} × ${item.quantity}`,
      lineTotal: item.dish.price * item.quantity,
    })),
  );
  readonly pendingCount = computed(() => this.participants().filter((person) => !person.isSubmitted).length);
  readonly hasAnyDishes = computed(() => this.participants().some((person) => person.items.length > 0));
  readonly isOpen = computed(() => this.status() === 'Open');
  readonly canSubmit = computed(() => this.isOpen() && this.consolidated().items.length > 0 && !this.submitting());
  readonly statusLabel = computed(() => {
    const status = this.status();
    return status ? STATUS_LABELS[status] : '';
  });
  readonly pendingBanner = computed(() => {
    const count = this.pendingCount();
    if (count === 0 || !this.isOpen()) {
      return '';
    }
    const subject = count === 1 ? `1 participant hasn't` : `${count} participants haven't`;
    return `${subject} submitted their cart yet — their dishes won't be included in the order`;
  });
  readonly cards = computed(() =>
    this.participants().map((person) => ({
      participant: person,
      isMe: person.id === this.ownerSessionId(),
      canRemove: this.isRemovable(person),
    })),
  );
  readonly submitLabel = computed(() => (this.submitting() ? 'Submitting…' : 'Submit'));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const reservationId = this.reservationId;
    if (!reservationId) {
      this.viewState.set('not-found');
      return;
    }

    this.viewState.set('loading');
    forkJoin({
      invite: this.groupOrdersService.getGroupInvite$(reservationId),
      // The backend answers 400 here when the group was submitted before an owner session existed.
      // The page works without it, so the request is silent: no error toast.
      ownerSessionId: this.groupOrdersService
        .getOwnerSessionId$(reservationId, { silent: true })
        .pipe(catchError(() => of(null))),
    })
      .pipe(
        switchMap(({ invite, ownerSessionId }) =>
          forkJoin({
            sessions: this.groupOrdersService.getSessions$(invite.GroupReservationId),
            status: this.groupOrdersService.getStatus$(invite.GroupReservationId),
          }).pipe(map((result) => ({ invite, ownerSessionId, ...result }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ invite, sessions, ownerSessionId, status }) => {
          this.invite.set(invite);
          this.participants.set(sessions.participants);
          this.ownerSessionId.set(ownerSessionId);
          this.status.set(status.status);
          this.lastUpdated.set(new Date());
          this.refreshFailed.set(false);
          this.viewState.set('ready');
          this.startSync();
        },
        error: (error: unknown) => this.viewState.set(this.loadErrorState(error)),
      });
  }

  refresh(): void {
    this.silentRefresh$().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }

  openSubmitDialog(): void {
    if (this.canSubmit()) {
      this.submitDialogOpen.set(true);
    }
  }

  closeSubmitDialog(): void {
    if (!this.submitting()) {
      this.submitDialogOpen.set(false);
    }
  }

  confirmSubmit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || !this.reservationId || this.submitting()) {
      return;
    }

    this.mutationCount++;
    this.submitting.set(true);
    this.groupOrdersService
      .submit$(groupId, this.reservationId)
      .pipe(
        finalize(() => {
          this.submitting.set(false);
          this.submitDialogOpen.set(false);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.status.set('Submitted');
          this.stopSync();
          this.toastService.success('Pre-order sent to the restaurant');
        },
        // The interceptor already shows the API error as a toast.
        error: () => undefined,
      });
  }

  askRemove(participant: Participant): void {
    if (this.isRemovable(participant)) {
      this.pendingRemoval.set(participant);
    }
  }

  closeRemoveDialog(): void {
    if (!this.removing()) {
      this.pendingRemoval.set(null);
    }
  }

  confirmRemove(): void {
    const groupId = this.invite()?.GroupReservationId;
    const participant = this.pendingRemoval();
    if (!groupId || !participant || this.removing()) {
      return;
    }

    this.mutationCount++;
    this.removing.set(true);
    this.groupOrdersService
      .removeSession$(groupId, participant.id)
      .pipe(
        finalize(() => {
          this.removing.set(false);
          this.pendingRemoval.set(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        // The response is a fresh server snapshot, so the sync line counts it as an update.
        next: (sessions) => {
          this.participants.set(sessions.participants);
          this.lastUpdated.set(new Date());
          this.refreshFailed.set(false);
        },
        // The interceptor already shows the API error as a toast.
        error: () => undefined,
      });
  }

  /** A forbidden reservation is shown as not found; an inactive session can't be retried. */
  private loadErrorState(error: unknown): 'not-found' | 'inactive' | 'error' {
    if (!(error instanceof HttpErrorResponse)) {
      return 'error';
    }
    if (error.status === 404 || error.status === 403) {
      return 'not-found';
    }
    if (error.status === 400 && error.error?.errorCode === 'GroupReservationExpired') {
      return 'inactive';
    }
    return 'error';
  }

  private isRemovable(participant: Participant): boolean {
    return participant.type === 'Guest' && this.isOpen();
  }

  private startSync(): void {
    if (this.syncSubscription || !this.isOpen()) {
      return;
    }
    this.syncSubscription = interval(SYNC_INTERVAL_MS)
      .pipe(
        exhaustMap(() => this.silentRefresh$()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  private stopSync(): void {
    this.syncSubscription?.unsubscribe();
    this.syncSubscription = null;
  }

  /** One silent refresh; does nothing while another refresh, a submit or a remove is running. */
  private silentRefresh$(): Observable<unknown> {
    return defer(() => {
      const groupId = this.invite()?.GroupReservationId;
      if (!groupId || !this.isOpen() || this.refreshing || this.submitting() || this.removing()) {
        return EMPTY;
      }

      const mutationAtStart = this.mutationCount;
      const isStale = () => mutationAtStart !== this.mutationCount || !this.isOpen();
      this.refreshing = true;
      return forkJoin({
        sessions: this.groupOrdersService.getSessions$(groupId, { silent: true }),
        status: this.groupOrdersService.getStatus$(groupId, { silent: true }),
      }).pipe(
        tap(({ sessions, status }) => {
          if (isStale()) {
            return;
          }
          this.participants.set(sessions.participants);
          this.status.set(status.status);
          this.lastUpdated.set(new Date());
          this.refreshFailed.set(false);
          if (status.status !== 'Open') {
            this.stopSync();
            this.submitDialogOpen.set(false);
            this.pendingRemoval.set(null);
          }
        }),
        catchError(() => {
          if (!isStale()) {
            this.refreshFailed.set(true);
          }
          return EMPTY;
        }),
        finalize(() => (this.refreshing = false)),
      );
    });
  }
}
