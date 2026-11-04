import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestSessionsService } from '@app/services/guest-sessions/guest-sessions.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GuestCartItem, GuestPreorderStore, PREORDER_CLOSED_MESSAGE } from './guest-preorder.store';

const TOKEN = 'tok-1';
const CART: GuestCartItem[] = [{ dishId: 'dish1', title: 'Caesar Salad', price: 12, qty: 1 }];

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

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

function storeSession(cart: GuestCartItem[] = CART): void {
  localStorage.setItem(`guestPreorder:${TOKEN}:guestName`, 'Ann');
  localStorage.setItem(`guestPreorder:${TOKEN}:cart`, JSON.stringify(cart));
  localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionId`, 'gs-1');
  localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionToken`, 'session-token');
}

/** Lets the store's awaited observables and follow-up calls settle. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('GuestPreorderStore', () => {
  let inviteService: jasmine.SpyObj<GuestInviteService>;
  let sessionsService: jasmine.SpyObj<GuestSessionsService>;
  let toast: jasmine.SpyObj<ToastService>;
  let store: GuestPreorderStore;

  beforeEach(() => {
    localStorage.clear();
    inviteService = jasmine.createSpyObj<GuestInviteService>('GuestInviteService', ['getInvite$']);
    sessionsService = jasmine.createSpyObj<GuestSessionsService>('GuestSessionsService', [
      'createSession', 'getMenu', 'addDish', 'removeDish', 'submit',
    ]);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    inviteService.getInvite$.and.returnValue(of(invite()));
    sessionsService.getMenu.and.returnValue(of({ dishes: {} }));

    TestBed.configureTestingModule({
      providers: [
        { provide: GuestInviteService, useValue: inviteService },
        { provide: GuestSessionsService, useValue: sessionsService },
        { provide: ToastService, useValue: toast },
      ],
    });
    store = TestBed.inject(GuestPreorderStore);
  });

  afterEach(() => localStorage.clear());

  describe('invite load', () => {
    it('requests the invite silently, without the interceptor toast', async () => {
      store.initContext(TOKEN);
      await settle();

      expect(inviteService.getInvite$).toHaveBeenCalledOnceWith(TOKEN, { silent: true });
    });

    it('404 → expired', async () => {
      inviteService.getInvite$.and.returnValue(throwError(() => httpError(404)));
      store.initContext(TOKEN);
      await settle();

      expect(store.inviteError()).toBe('expired');
    });

    it('400 GroupReservationExpired → expired', async () => {
      inviteService.getInvite$.and.returnValue(throwError(() => httpError(400, { errorCode: 'GroupReservationExpired' })));
      store.initContext(TOKEN);
      await settle();

      expect(store.inviteError()).toBe('expired');
    });

    it('500 → retryable', async () => {
      inviteService.getInvite$.and.returnValue(throwError(() => httpError(500)));
      store.initContext(TOKEN);
      await settle();

      expect(store.inviteError()).toBe('retryable');
    });
  });

  describe('full group', () => {
    it('Open with no places left and no stored session → full, no menu request', async () => {
      inviteService.getInvite$.and.returnValue(of(invite({ AvailableSpots: 0 })));
      store.initContext(TOKEN);
      await settle();

      expect(store.isFull()).toBeTrue();
      expect(sessionsService.getMenu).not.toHaveBeenCalled();
    });

    it('Open with no places left but a stored session → not full, the menu loads', async () => {
      storeSession();
      inviteService.getInvite$.and.returnValue(of(invite({ AvailableSpots: 0 })));
      store.initContext(TOKEN);
      await settle();

      expect(store.isFull()).toBeFalse();
      expect(sessionsService.getMenu).toHaveBeenCalledOnceWith('gs-1', 'session-token');
      expect(store.cartItems()).toEqual(CART);
    });

    it('a stored session keeps the cart working when places are 0', async () => {
      storeSession();
      inviteService.getInvite$.and.returnValue(of(invite({ AvailableSpots: 0 })));
      sessionsService.addDish.and.returnValue(of({}));
      store.initContext(TOKEN);
      await settle();

      expect(await store.inc('dish1')).toBeTrue();
      expect(store.cartItems()[0].qty).toBe(2);
    });

    it('join → 400 GuestLimitExceeded → full, silent request, no toast', async () => {
      sessionsService.createSession.and.returnValue(throwError(() => httpError(400, { errorCode: 'GuestLimitExceeded' })));
      store.initContext(TOKEN);
      await settle();

      await store.join('Ann');

      expect(sessionsService.createSession).toHaveBeenCalledOnceWith(
        { groupReservationId: 'grp-1', guestName: 'Ann' },
        { silent: true },
      );
      expect(store.isFull()).toBeTrue();
      expect(store.joinError()).toBeNull();
      expect(toast.error).not.toHaveBeenCalled();
    });

    it('join → another error → not full, the store shows the server message as a toast', async () => {
      sessionsService.createSession.and.returnValue(throwError(() => httpError(500, { error: 'Internal Server Error' })));
      store.initContext(TOKEN);
      await settle();

      await store.join('Ann');

      expect(store.isFull()).toBeFalse();
      expect(store.joinError()).toBe('Failed to join session');
      expect(toast.error).toHaveBeenCalledOnceWith('Internal Server Error', 'Error');
    });
  });

  describe('submitted group', () => {
    it('Submitted → closed, no menu request even with a stored session, stored cart kept', async () => {
      storeSession();
      inviteService.getInvite$.and.returnValue(of(invite({ Status: 'Submitted', AvailableSpots: 0 })));
      store.initContext(TOKEN);
      await settle();

      expect(store.isClosed()).toBeTrue();
      expect(sessionsService.getMenu).not.toHaveBeenCalled();
      expect(store.cartItems()).toEqual(CART);
    });

    it('cart change → 409 → closed message, local cart unchanged', async () => {
      storeSession();
      sessionsService.addDish.and.returnValue(throwError(() => httpError(409, { error: 'Group reservation was submitted.' })));
      store.initContext(TOKEN);
      await settle();

      expect(await store.inc('dish1')).toBeFalse();

      expect(store.cartError()).toBe(PREORDER_CLOSED_MESSAGE);
      expect(store.cartItems()).toEqual(CART);
    });

    it('cart change → 500 keeps the generic cart error', async () => {
      storeSession();
      sessionsService.removeDish.and.returnValue(throwError(() => httpError(500)));
      store.initContext(TOKEN);
      await settle();

      await store.dec('dish1');

      expect(store.cartError()).toBe('Could not update your cart. Please try again.');
      expect(store.cartItems()).toEqual(CART);
    });

    it('cart submit → 409 → closed message, still active with the same cart', async () => {
      storeSession();
      sessionsService.submit.and.returnValue(throwError(() => httpError(409)));
      store.initContext(TOKEN);
      await settle();

      await store.submitPreorder();

      expect(store.cartError()).toBe(PREORDER_CLOSED_MESSAGE);
      expect(store.status()).toBe('active');
      expect(store.cartItems()).toEqual(CART);
    });
  });
});
