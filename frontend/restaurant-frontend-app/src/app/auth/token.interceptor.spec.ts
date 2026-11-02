import { HttpClient, HttpContext, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';
const GROUP_URL = 'https://test.api/guest/groups/grp-1';
const TOKENS = { idToken: 'id-token', accessToken: 'access-token' };

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;
  let authService: jasmine.SpyObj<AuthenticationService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });

  it('adds the Cognito token to the owner group sessions list', () => {
    authService.getTokens.and.returnValue(TOKENS);
    http.get(`${GROUP_URL}/sessions`).subscribe();

    const req = httpMock.expectOne(`${GROUP_URL}/sessions`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
    req.flush({});
  });

  it('adds the Cognito token to Remove Guest', () => {
    authService.getTokens.and.returnValue(TOKENS);
    http.delete(`${GROUP_URL}/sessions/guest-1`).subscribe();

    const req = httpMock.expectOne(`${GROUP_URL}/sessions/guest-1`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
    req.flush({});
  });

  it('keeps the guest session token on guest endpoints', () => {
    authService.getTokens.and.returnValue(TOKENS);
    const url = 'https://test.api/guest/sessions/guest-1';
    http.get(url, { headers: new HttpHeaders({ Authorization: 'Bearer guest-token' }) }).subscribe();

    const req = httpMock.expectOne(url);
    expect(req.request.headers.get('Authorization')).toBe('Bearer guest-token');
    req.flush({});
  });

  it('does not toast a silent request to a guest endpoint', () => {
    let receivedStatus: number | undefined;
    http
      .get(`${GROUP_URL}/status`, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(`${GROUP_URL}/status`)
      .flush({ error: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(500);
  });
});
