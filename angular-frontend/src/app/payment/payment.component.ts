import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PaymentService } from '../services/payment.service';
import { AuthService } from '../auth/auth.service';
import { Router } from '@angular/router';
import { interval, Subscription, switchMap, takeWhile } from 'rxjs';

@Component({
  selector: 'app-payment',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './payment.component.html'
})
export class PaymentComponent implements OnDestroy {
  email = '';
  amountNaira: number | null = null;

  loading = false;
  error = '';
  authorizationUrl = '';
  reference = '';
  status = ''; // Pending | Success | Failed

  private pollSub?: Subscription;

  constructor(
    private paymentService: PaymentService,
    private auth: AuthService,
    private router: Router
  ) {
    const user = this.auth['currentUserSubject']?.value;
    if (user?.email) {
      this.email = user.email;
    }
  }

  onSubmit(): void {
    if (!this.amountNaira || this.amountNaira <= 0) {
      this.error = 'Enter a valid amount';
      return;
    }

    this.error = '';
    this.loading = true;
    this.authorizationUrl = '';
    this.status = '';

    // Because PaymentService generates a fresh idempotency key per call, clicking "Pay"
    // twice in a row (e.g. impatient double-click) creates two DIFFERENT requests here —
    // that's expected. The idempotency guard protects against the SAME request being
    // retried (e.g. the browser auto-retrying a timed-out network call with the same key),
    // not against the user deliberately submitting twice.
    this.paymentService.initiatePayment(this.email, this.amountNaira).subscribe({
      next: (res) => {
        this.loading = false;
        this.authorizationUrl = res.authorizationUrl;
        this.reference = res.reference;
        this.startPolling();
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'Payment initiation failed. Please try again.';
      }
    });
  }

  // Polls our own backend (not Paystack directly) for status, since our backend is the
  // one that independently verifies with Paystack via the webhook -> verify flow.
  private startPolling(): void {
    this.pollSub?.unsubscribe();

    this.pollSub = interval(3000)
      .pipe(
        switchMap(() => this.paymentService.getTransactionStatus(this.reference)),
        takeWhile((txn) => txn.status === 0 /* Pending */, true) // include the final emission
      )
      .subscribe({
        next: (txn) => {
          this.status = ['Pending', 'Success', 'Failed'][txn.status] ?? 'Unknown';
          if (this.status !== 'Pending') {
            this.pollSub?.unsubscribe();
          }
        },
        error: () => {
          this.pollSub?.unsubscribe();
        }
      });
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }
}
