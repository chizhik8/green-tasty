import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestSessionsService } from '@app/services/guest-sessions/guest-sessions.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GuestPreOrderPageComponent } from './guest-pre-order-page.component';

const TOKEN = 'tok-1';

function invite(overrides: Partial<GuestInvite> = {}): GuestInvite {
  return {
    GroupReservationId: 'grp-1',
    LocationId: 'loc-1',
    RestaurantName: 'Green & Tasty',
    LocationAddress: 'Rustaveli Avenue 48',
    StartTime: '2026-10-21T16:00:00Z',
    EndTime: '2026-10-21T17:30:00Z',
    MaxGuestCount: 4,
    TableNumber: 7,
    InviterName: 'Qa Tester',
    Status: 'Open',
    AvailableSpots: 2,
    ...overrides,
  };
}

function storeSession(): void {
  localStorage.setItem(`guestPreorder:${TOKEN}:guestName`, 'Ann');
  localStorage.setItem(
    `guestPreorder:${TOKEN}:cart`,
    JSON.stringify([{ dishId: 'dish1', title: 'Caesar Salad', price: 12, qty: 1 }]),
  );
  localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionId`, 'gs-1');
  localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionToken`, 'session-token');
}

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === text);
}

const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('GuestPreOrderPageComponent', () => {
  let inviteService: jasmine.SpyObj<GuestInviteService>;
  let sessionsService: jasmine.SpyObj<GuestSessionsService>;

  async function create() {
    TestBed.configureTestingModule({
      imports: [GuestPreOrderPageComponent],
      providers: [
        provideRouter([]),
        { provide: GuestInviteService, useValue: inviteService },
        { provide: GuestSessionsService, useValue: sessionsService },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['success', 'error', 'info']) },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ token: TOKEN })) } },
      ],
    });
    const router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const fixture = TestBed.createComponent(GuestPreOrderPageComponent);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    return { fixture, router, element: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    localStorage.clear();
    inviteService = jasmine.createSpyObj<GuestInviteService>('GuestInviteService', ['getInvite$']);
    sessionsService = jasmine.createSpyObj<GuestSessionsService>('GuestSessionsService', [
      'createSession', 'getMenu', 'addDish', 'removeDish', 'submit',
    ]);
    inviteService.getInvite$.and.returnValue(of(invite()));
    sessionsService.getMenu.and.returnValue(of({ dishes: {} }));
  });

  afterEach(() => localStorage.clear());

  describe('invite errors', () => {
    it('404 redirects to /guest-pre-order-expired', async () => {
      inviteService.getInvite$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      const { router } = await create();

      expect(router.navigateByUrl).toHaveBeenCalledWith('/guest-pre-order-expired');
    });

    it('400 GroupReservationExpired redirects to /guest-pre-order-expired', async () => {
      inviteService.getInvite$.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'GroupReservationExpired' } })),
      );
      const { router } = await create();

      expect(router.navigateByUrl).toHaveBeenCalledWith('/guest-pre-order-expired');
    });

    it('500 shows the retry block', async () => {
      inviteService.getInvite$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const { router, element } = await create();

      expect(buttonByText(element, 'Try again')).toBeDefined();
      expect(router.navigateByUrl).not.toHaveBeenCalled();
    });
  });

  describe('full group', () => {
    it('no places left and no stored session: full block, no name modal, no menu, no Try again', async () => {
      inviteService.getInvite$.and.returnValue(of(invite({ AvailableSpots: 0 })));
      const { element } = await create();

      expect(element.textContent).toContain('This group is full');
      expect(element.textContent).toContain(
        'All places at this table are already taken. Ask the organizer if you should still join.',
      );
      expect(element.querySelector('app-guest-name-modal')).toBeNull();
      expect(element.querySelector('.menu-container')).toBeNull();
      expect(buttonByText(element, 'Try again')).toBeUndefined();
    });

    it('no places left but a stored session: the menu page as before', async () => {
      storeSession();
      inviteService.getInvite$.and.returnValue(of(invite({ AvailableSpots: 0 })));
      const { element } = await create();

      expect(element.textContent).not.toContain('This group is full');
      expect(element.querySelector('.menu-container')).not.toBeNull();
      expect(sessionsService.getMenu).toHaveBeenCalledOnceWith('gs-1', 'session-token');
    });

    it('join → 400 GuestLimitExceeded shows the full block', async () => {
      sessionsService.createSession.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'GuestLimitExceeded' } })),
      );
      const { fixture, element } = await create();
      expect(element.querySelector('app-guest-name-modal')).not.toBeNull();

      await fixture.componentInstance.store.join('Ann');
      fixture.detectChanges();

      expect(element.textContent).toContain('This group is full');
      expect(element.textContent).not.toContain('Failed to join session');
      expect(element.querySelector('app-guest-name-modal')).toBeNull();
    });
  });

  describe('submitted group', () => {
    it('closed message, stored cart read-only, no name modal, no menu request', async () => {
      storeSession();
      inviteService.getInvite$.and.returnValue(of(invite({ Status: 'Submitted', AvailableSpots: 0 })));
      const { element } = await create();

      expect(element.textContent).toContain('Pre-order is closed, changes are no longer possible');
      expect(element.textContent).toContain('Caesar Salad × 1');
      expect(element.querySelector('[aria-label="Increase quantity"]')).toBeNull();
      expect(element.querySelector('[aria-label="Open cart"]')).toBeNull();
      expect(element.querySelector('app-guest-name-modal')).toBeNull();
      expect(sessionsService.getMenu).not.toHaveBeenCalled();
    });

    it('closed message without a cart for a browser that never joined', async () => {
      inviteService.getInvite$.and.returnValue(of(invite({ Status: 'Submitted', AvailableSpots: 0 })));
      const { element } = await create();

      expect(element.textContent).toContain('Pre-order is closed, changes are no longer possible');
      expect(element.querySelector('.closed-cart')).toBeNull();
      expect(element.querySelector('app-guest-name-modal')).toBeNull();
    });

    it('a cart change answered 409 shows the closed message in the cart drawer, cart unchanged', async () => {
      storeSession();
      sessionsService.addDish.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      const { fixture, element } = await create();

      (element.querySelector('[aria-label="Open cart"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      (element.querySelector('[aria-label="Increase quantity"]') as HTMLButtonElement).click();
      await settle();
      fixture.detectChanges();

      const drawer = element.querySelector('app-guest-cart-drawer')!;
      expect(drawer.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
        'Pre-order is closed, changes are no longer possible',
      );
      expect(drawer.querySelector('.qty-value')?.textContent?.trim()).toBe('1');
    });
  });
});
