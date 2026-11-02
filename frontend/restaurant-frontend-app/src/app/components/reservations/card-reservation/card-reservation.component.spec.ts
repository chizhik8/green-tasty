import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CardReservationComponent } from './card-reservation.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';
import { Roles } from '@app/shared/constants/roles';

const RESERVATION: ReservationCustomer = {
  id: 'res-1',
  tableId: 'table-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 2,
  status: ReservationStatusCustomer.Reserved,
  restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48',
  waiterId: 'waiter-1',
};

function groupPreOrderLink(element: HTMLElement): HTMLAnchorElement | undefined {
  return [...element.querySelectorAll('a')].find((link) => link.textContent?.trim() === 'Group Pre-Order');
}

describe('CardReservationComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }, provideRouter([])],
      imports: [CardReservationComponent],
    }).compileComponents();
  });

  // r — обов'язковий input: без нього компонент падає з NG0950
  function render(reservation: ReservationCustomer, role: string = Roles.CUSTOMER): HTMLElement {
    const fixture = TestBed.createComponent(CardReservationComponent);
    fixture.componentRef.setInput('r', reservation);
    fixture.componentRef.setInput('role', role);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the Group Pre-Order link for a customer with a reserved booking for 2+ guests', () => {
    const link = groupPreOrderLink(render(RESERVATION));

    expect(link).toBeDefined();
    expect(link?.getAttribute('href')).toMatch(/\/reservations\/res-1\/group-order$/);
  });

  it('hides the link when the reservation is for one guest', () => {
    expect(groupPreOrderLink(render({ ...RESERVATION, guestCount: 1 }))).toBeUndefined();
  });

  it('hides the link when the reservation is not Reserved', () => {
    expect(groupPreOrderLink(render({ ...RESERVATION, status: ReservationStatusCustomer.InProgress }))).toBeUndefined();
  });

  it('hides the link for a waiter', () => {
    expect(groupPreOrderLink(render(RESERVATION, Roles.WAITER))).toBeUndefined();
  });
});
