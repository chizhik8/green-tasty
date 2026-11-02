import { TestBed } from '@angular/core/testing';
import { Participant } from '@app/models/group-orders/participant.model';
import { ParticipantCardComponent } from './participant-card.component';

const ALEX: Participant = {
  id: 'alex',
  type: 'Guest',
  name: 'Alex',
  isSubmitted: true,
  total: 14,
  items: [{ dish: { id: 'dish1', name: 'Quinoa Salad', price: 14 }, quantity: 1 }],
};

function create(inputs: { isMe?: boolean; canRemove?: boolean } = {}) {
  const fixture = TestBed.createComponent(ParticipantCardComponent);
  fixture.componentRef.setInput('participant', ALEX);
  fixture.componentRef.setInput('isMe', inputs.isMe ?? false);
  fixture.componentRef.setInput('canRemove', inputs.canRemove ?? false);
  fixture.detectChanges();
  return { fixture, element: fixture.nativeElement as HTMLElement };
}

function removeButton(element: HTMLElement): HTMLButtonElement | undefined {
  return [...element.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Remove Guest');
}

describe('ParticipantCardComponent (extra)', () => {
  it('titles the owner\'s own card "My Pre-Order"', () => {
    const { element } = create({ isMe: true });
    expect(element.querySelector('h2')?.textContent?.trim()).toBe('My Pre-Order');
  });

  it('shows no Remove Guest button when canRemove is false', () => {
    expect(removeButton(create().element)).toBeUndefined();
  });

  it('emits removeClick with the participant when Remove Guest is clicked', () => {
    const { fixture, element } = create({ canRemove: true });
    const emitted: Participant[] = [];
    fixture.componentInstance.removeClick.subscribe((participant) => emitted.push(participant));

    removeButton(element)?.click();

    expect(emitted).toEqual([ALEX]);
  });
});
