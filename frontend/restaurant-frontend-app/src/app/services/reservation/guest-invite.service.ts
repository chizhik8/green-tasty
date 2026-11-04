import { HttpClient, HttpContext } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { GroupInviteToken, GuestInvite } from '@app/models/reservations/guest-invite.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

@Injectable({
  providedIn: 'root',
})
export class GuestInviteService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);

  getInviteToken$(reservationId: string): Observable<GroupInviteToken> {
    return this.http.get<GroupInviteToken>(
      `${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/invite-token`,
    );
  }

  /** `silent: true`: the interceptor shows no error toast, the caller shows the state itself. */
  getInvite$(token: string, options?: { silent?: boolean }): Observable<GuestInvite> {
    return this.http.get<GuestInvite>(`${this.baseUrl}/guest/invites/${encodeURIComponent(token)}`, {
      context: new HttpContext().set(SKIP_ERROR_TOAST, options?.silent === true),
    });
  }
}
