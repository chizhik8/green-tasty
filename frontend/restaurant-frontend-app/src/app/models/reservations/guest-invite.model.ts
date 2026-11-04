export interface GuestInvite {
  GroupReservationId: string;
  LocationId: string;
  RestaurantName: string;
  LocationAddress: string;
  StartTime: string;
  EndTime: string;
  MaxGuestCount: number;
  TableNumber: number;
  InviterName: string;
  /** A `Closed` group is answered with `400 GroupReservationExpired`, not with an invite. */
  Status: 'Open' | 'Submitted';
  /** How many more guests can join now; `0` for a full or `Submitted` group. */
  AvailableSpots: number;
}

export interface GroupInviteToken {
  token: string;
}
