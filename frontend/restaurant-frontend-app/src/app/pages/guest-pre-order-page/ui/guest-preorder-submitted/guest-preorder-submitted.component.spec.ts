import { TestBed } from '@angular/core/testing';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestPreorderSubmittedComponent } from './guest-preorder-submitted.component';

function render(invite: Partial<GuestInvite> | null): string {
  const fixture = TestBed.createComponent(GuestPreorderSubmittedComponent);
  fixture.componentRef.setInput('invite', invite as GuestInvite | null);
  fixture.detectChanges();
  return (fixture.nativeElement as HTMLElement).querySelector('.subtitle')?.textContent ?? '';
}

describe('GuestPreorderSubmittedComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [GuestPreorderSubmittedComponent] }));

  it("names the inviter's table", () => {
    expect(render({ InviterName: 'Qa Tester' })).toContain("Your selections have been added to Qa Tester's table.");
  });

  it('falls back to "your group\'s table" when the inviter is Unknown', () => {
    expect(render({ InviterName: 'Unknown' })).toContain("Your selections have been added to your group's table.");
  });

  it('falls back to "your group\'s table" when the inviter name is missing', () => {
    expect(render({ InviterName: '' })).toContain("added to your group's table.");
    expect(render(null)).toContain("added to your group's table.");
  });
});
