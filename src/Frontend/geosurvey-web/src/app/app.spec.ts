import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { session: signal(null), notice: signal(''), signingOut: signal(false) },
        },
      ],
    }).compileComponents();
  });

  it('provides a skip link and distinct platform and organization entry points', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.skip-link')?.getAttribute('href')).toBe('#main');
    expect(compiled.querySelector('a[href="/platform/sign-in"]')?.textContent).toContain(
      'Platform',
    );
    expect(compiled.querySelector('a[href="/sign-in"]')?.textContent).toContain('Organization');
  });
});
