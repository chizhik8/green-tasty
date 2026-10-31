using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public interface IGuestSessionRepository
{
    /// <summary>
    /// Counts the joined guests of a group reservation: its guest sessions, excluding owner sessions.
    /// </summary>
    Task<int> CountJoinedGuestsByGroupReservationIdAsync(string groupReservationId);

    Task<GuestSession?> GetByGroupReservationId(string groupReservationId);

    Task<GuestSession?> GetByGuestSessionId(string id);

    Task<GuestSession?> GetByTokenHash(string tokenHash);

    Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId);

    Task SaveAsync(GuestSession guestSession);

    Task DeleteAsync(GuestSession session);
}
