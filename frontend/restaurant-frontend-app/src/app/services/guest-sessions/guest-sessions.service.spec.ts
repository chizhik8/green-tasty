import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { CreateGuestSessionResponse, GuestSessionsService } from './guest-sessions.service';

const BASE_URL = 'https://test.api';

describe('GuestSessionsService', () => {
  let service: GuestSessionsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: BASE_URL }],
    });
    service = TestBed.inject(GuestSessionsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('creates a guest session with POST and the body as given', () => {
    let result: CreateGuestSessionResponse | undefined;

    service.createSession({ groupReservationId: 'grp-1', guestName: 'Ann' }).subscribe((response) => (result = response));

    const req = httpMock.expectOne(`${BASE_URL}/guest/sessions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ groupReservationId: 'grp-1', guestName: 'Ann' });
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeFalse();
    req.flush({ SessionId: 'gs-1', SessionToken: 'tkn' });
    expect(result).toEqual({ SessionId: 'gs-1', SessionToken: 'tkn' });
  });

  it('marks the create-session request silent when asked', () => {
    service.createSession({ groupReservationId: 'grp-1', guestName: 'Ann' }, { silent: true }).subscribe();

    const req = httpMock.expectOne(`${BASE_URL}/guest/sessions`);
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeTrue();
    req.flush({ SessionId: 'gs-1', SessionToken: 'tkn' });
  });
});
