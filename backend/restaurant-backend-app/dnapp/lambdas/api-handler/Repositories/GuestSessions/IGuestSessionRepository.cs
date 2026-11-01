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

    /// <summary>
    /// Loads a session by its id with a strongly consistent read.
    /// </summary>
    Task<GuestSession?> GetByGuestSessionIdConsistentAsync(string id);

    Task<GuestSession?> GetByTokenHash(string tokenHash);

    Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId);

    Task SaveAsync(GuestSession guestSession);

    /// <summary>
    /// Creates the session only if no session with its id exists (conditional put).
    /// </summary>
    /// <returns><c>true</c> if the session was created; <c>false</c> if a session with this id already exists.</returns>
    Task<bool> TryCreateAsync(GuestSession guestSession);

    Task DeleteAsync(GuestSession session);
}
