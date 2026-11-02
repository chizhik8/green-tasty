import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { NEVER, Subject, of, throwError } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const SYNC_MS = 15_000;
const caesar = { id: 'dish1', name: 'Caesar Salad', price: 180 };
const tomYum = { id: 'dish2', name: 'Tom Yum', price: 220 };

const SESSIONS: GroupSessionsResponse = {
  participants: [
    { id: 'owner', type: 'Owner', name: 'Yevhenii', isSubmitted: true, total: 180, items: [{ dish: caesar, quantity: 1 }] },
    { id: 'andrii', type: 'Guest', name: 'Andrii', isSubmitted: false, total: 360, items: [{ dish: caesar, quantity: 2 }] },
    { id: 'olena', type: 'Guest', name: 'Olena', isSubmitted: false, total: 220, items: [{ dish: tomYum, quantity: 1 }] },
  ],
  total: 760,
};
const INVITE = {
  GroupReservationId: 'grp-1',
  LocationAddress: 'Rustaveli Avenue 48',
  MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
} as GuestInvite;

function status(value: GroupStatusResponse['status']): GroupStatusResponse {
  return { currentParticipants: 3, maxCapacity: 5, status: value };
}

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === text);
}

describe('GroupOrderPageComponent (extra)', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  let toast: jasmine.SpyObj<ToastService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        { provide: ToastService, useValue: toast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } } },
      ],
    });
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function confirmSubmit(fixture: ReturnType<typeof create>['fixture'], element: HTMLElement): void {
    buttonByText(element, 'Submit Pre-Order')?.click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')?.click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$', 'getSessions$', 'getOwnerSessionId$', 'getStatus$', 'submit$', 'removeSession$',
    ]);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.returnValue(of(structuredClone(SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
    service.getStatus$.and.returnValue(of(status('Open')));
  });

  it('uses the plural form when two participants have not submitted', () => {
    expect(create().element.textContent).toContain(
      "2 participants haven't submitted their cart yet — their dishes won't be included in the order",
    );
  });

  it('Closed group: badge "Closed", no Submit Pre-Order, no Remove Guest', () => {
    service.getStatus$.and.returnValue(of(status('Closed')));
    const { element } = create();

    expect(element.querySelector('.badge')?.textContent?.trim()).toBe('Closed');
    expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
    expect(buttonByText(element, 'Remove Guest')).toBeUndefined();
  });

  it('renders the participants without "My Pre-Order" when getOwnerSessionId$ fails', () => {
    service.getOwnerSessionId$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    const { element } = create();

    expect(element.querySelectorAll('app-participant-card').length).toBe(3);
    expect(element.textContent).not.toContain('My Pre-Order');
  });

  it('shows the success toast after a successful submit', () => {
    service.submit$.and.returnValue(of([{ dishId: 'dish1', quantity: 1 }]));
    const { fixture, element } = create();

    confirmSubmit(fixture, element);

    expect(toast.success).toHaveBeenCalledOnceWith('Pre-order sent to the restaurant');
  });

  it('adds no error toast of its own when submit fails', () => {
    service.submit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    const { fixture, element } = create();

    confirmSubmit(fixture, element);

    expect(toast.error).not.toHaveBeenCalled();
  });

  it('Cancel closes the submit dialog without a request', () => {
    const { fixture, element } = create();
    buttonByText(element, 'Submit Pre-Order')?.click();
    fixture.detectChanges();

    buttonByText(element.querySelector('[role="dialog"]')!, 'Cancel')?.click();
    fixture.detectChanges();

    expect(element.querySelector('[role="dialog"]')).toBeNull();
    expect(service.submit$).not.toHaveBeenCalled();
  });

  it('stops the sync when a background refresh returns Submitted', fakeAsync(() => {
    create();
    service.getStatus$.and.returnValue(of(status('Submitted')));
    tick(SYNC_MS);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);

    tick(SYNC_MS * 3);

    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    discardPeriodicTasks();
  }));

  it('Try again after an error starts a single timer', fakeAsync(() => {
    service.getGroupInvite$.and.returnValues(throwError(() => new HttpErrorResponse({ status: 500 })), of(INVITE));
    const { fixture, element } = create();
    buttonByText(element, 'Try again')?.click();
    fixture.detectChanges();

    tick(SYNC_MS);

    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    discardPeriodicTasks();
  }));

  it('Refresh does not start a second request while a background one is pending', fakeAsync(() => {
    const { fixture, element } = create();
    service.getSessions$.and.returnValue(NEVER);
    tick(SYNC_MS);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);

    buttonByText(element, 'Refresh')?.click();
    fixture.detectChanges();

    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    discardPeriodicTasks();
  }));

  it('drops a background refresh that started before Remove Guest', fakeAsync(() => {
    const pending$ = new Subject<GroupSessionsResponse>();
    service.removeSession$.and.returnValue(of({ participants: SESSIONS.participants.slice(0, 2), total: 540 }));
    const { fixture, element } = create();
    service.getSessions$.and.returnValue(pending$);
    tick(SYNC_MS);

    buttonByText(element, 'Remove Guest')?.click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')?.click();
    fixture.detectChanges();
    pending$.next(structuredClone(SESSIONS));
    pending$.complete();
    fixture.detectChanges();

    expect(element.querySelectorAll('app-participant-card').length).toBe(2);
    discardPeriodicTasks();
  }));

  it('a successful Remove Guest clears "Couldn\'t refresh"', fakeAsync(() => {
    service.removeSession$.and.returnValue(of({ participants: SESSIONS.participants.slice(0, 2), total: 540 }));
    const { fixture, element } = create();
    service.getSessions$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    tick(SYNC_MS);
    fixture.detectChanges();
    expect(element.textContent).toContain("Couldn't refresh");

    buttonByText(element, 'Remove Guest')?.click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')?.click();
    fixture.detectChanges();

    expect(element.textContent).not.toContain("Couldn't refresh");
    expect(element.textContent).toContain('Updated at');
    discardPeriodicTasks();
  }));
});
