import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface InitiatePaymentResponse {
  authorizationUrl: string;
  reference: string;
}

@Injectable({ providedIn: 'root' })
export class PaymentService {
  constructor(private http: HttpClient) {}

  initiatePayment(email: string, amountNaira: number): Observable<InitiatePaymentResponse> {
    // Generated ONCE per user click, not regenerated on the browser's own network retry —
    // this is the client-side half of the idempotency guarantee described in the backend.
    const idempotencyKey = this.generateIdempotencyKey();
    const amountKobo = Math.round(amountNaira * 100);

    return this.http.post<InitiatePaymentResponse>(`${environment.apiBaseUrl}/payments/initiate`, {
      email,
      amountKobo,
      idempotencyKey
    });
  }

  getTransactionStatus(reference: string): Observable<any> {
    return this.http.get(`${environment.apiBaseUrl}/payments/${reference}`);
  }

  private generateIdempotencyKey(): string {
    // crypto.randomUUID() is available in all evergreen browsers.
    return crypto.randomUUID();
  }
}
