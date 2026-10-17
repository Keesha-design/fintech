import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AuthResponse {
  token: string;
  email: string;
  role: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenKey = 'fintech_token';
  private currentUserSubject = new BehaviorSubject<AuthResponse | null>(this.readStoredUser());
  currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {}

  register(email: string, password: string): Observable<any> {
    return this.http.post(`${environment.apiBaseUrl}/auth/register`, { email, password });
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { email, password })
      .pipe(
        tap((res) => {
          // NOTE: storing a JWT in localStorage is convenient for a demo but is vulnerable
          // to XSS. A production bank app would prefer an httpOnly, Secure, SameSite cookie
          // issued by the backend instead — flagged here as a deliberate demo simplification.
          localStorage.setItem(this.tokenKey, res.token);
          localStorage.setItem('fintech_user', JSON.stringify(res));
          this.currentUserSubject.next(res);
        })
      );
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem('fintech_user');
    this.currentUserSubject.next(null);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  private readStoredUser(): AuthResponse | null {
    const raw = localStorage.getItem('fintech_user');
    return raw ? JSON.parse(raw) : null;
  }
}
