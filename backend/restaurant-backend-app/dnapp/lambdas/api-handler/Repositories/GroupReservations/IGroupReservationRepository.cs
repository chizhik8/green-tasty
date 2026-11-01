using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupReservations;

public interface IGroupReservationRepository
{
    Task<GroupReservation?> GetByReservationIdAsync(string reservationId);
    Task<GroupReservation?> GetGroupReservationByIdAsync(string groupReservationId);
    Task DeleteGroupReservationAsync(GroupReservation groupReservation);
    Task SaveGroupReservationAsync(GroupReservation groupReservation);
    Task<GroupReservation?> GetGroupReservationByTokenAsync(string token);

    /// <summary>
    /// Writes the whole order and the group reservation status in one DynamoDB transaction:
    /// a <c>Put</c> of the order and an <c>Update</c> that sets the group's <c>status</c> to
    /// <paramref name="groupReservation"/>.<see cref="GroupReservation.Status"/>, on the condition
    /// that the stored status is <paramref name="expectedCurrentStatus"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if both items were written; <c>false</c> if the group status condition failed,
    /// in which case nothing was written. Any other failure is thrown.
    /// </returns>
    Task<bool> TryUpdateStatusWithOrderAsync(GroupReservation groupReservation, GroupReservationStatus expectedCurrentStatus, Order order);
}
