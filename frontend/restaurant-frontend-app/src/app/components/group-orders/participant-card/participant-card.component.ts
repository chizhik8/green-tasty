import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Participant } from '@app/models/group-orders/participant.model';

@Component({
  selector: 'app-participant-card',
  imports: [CurrencyPipe],
  templateUrl: './participant-card.component.html',
  styleUrl: './participant-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ParticipantCardComponent {
  readonly participant = input.required<Participant>();
  readonly isMe = input(false);
  readonly canRemove = input(false);

  readonly removeClick = output<Participant>();

  readonly title = computed(() => {
    const person = this.participant();
    if (this.isMe()) {
      return 'My Pre-Order';
    }
    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });

  readonly rows = computed(() =>
    this.participant().items.map((item) => ({
      dish: item.dish,
      quantity: item.quantity,
      initial: item.dish.name.charAt(0).toUpperCase(),
      lineTotal: item.dish.price * item.quantity,
    })),
  );

  readonly removeLabel = computed(() => `Remove ${this.participant().name}`);

  onRemove(): void {
    this.removeClick.emit(this.participant());
  }
}
