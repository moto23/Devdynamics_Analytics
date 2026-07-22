import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpHeaders } from '@angular/common/http';
import { Apollo, gql } from 'apollo-angular';
import { firstValueFrom } from 'rxjs';

const STORAGE_KEY = 'devdynamics.adminKey';

/**
 * Holds the admin API key used to authorise management mutations.
 *
 * Stored in sessionStorage, not localStorage: the key should not outlive the
 * browser session on a shared machine.
 *
 * Management controls are hidden until `isAuthenticated` is true. Showing an
 * action that is guaranteed to fail is worse than not showing it, so the UI
 * gates on this rather than letting the server reject the click.
 */
/*
 * Provided by ShellComponent, not the root injector: this service injects
 * Apollo, which is supplied at the shell so the landing page does not ship the
 * GraphQL client, and a root-scoped service cannot resolve a
 * component-provided dependency.
 */
@Injectable()
export class AdminService {

  private readonly apollo = inject(Apollo);

  private readonly key = signal<string | null>(this.read());

  readonly isAuthenticated = computed(() => !!this.key());

  /** Never rendered in full — only ever as a masked hint. */
  readonly maskedKey = computed(() => {
    const value = this.key();
    if (!value) return null;
    return value.length <= 8 ? '••••' : `${value.slice(0, 4)}…${value.slice(-4)}`;
  });

  /**
   * Validates the key against the API before storing it, so a wrong key is
   * rejected here rather than surfacing later as a failed action.
   */
  async signIn(candidate: string): Promise<{ ok: boolean; message: string }> {
    const trimmed = candidate.trim();

    if (!trimmed) {
      return { ok: false, message: 'Enter an admin key.' };
    }

    try {
      // setRepositoryActive on a non-existent id: authorisation runs first, so
      // a valid key returns a normal "not found" result while an invalid one
      // raises UNAUTHORIZED. Nothing is modified either way.
      const result = await firstValueFrom(this.apollo.mutate<any>({
        mutation: gql`
          mutation ($id: Int!) {
            setRepositoryActive(id: $id, isActive: true) { success message }
          }
        `,
        variables: { id: -1 },
        context: { headers: new HttpHeaders({ 'X-Admin-Key': trimmed }) },
        errorPolicy: 'all'
      }));

      const code = firstErrorCode(result);

      if (code === 'UNAUTHORIZED') {
        return { ok: false, message: 'That key was not accepted.' };
      }

      if (code === 'ADMIN_KEY_NOT_CONFIGURED') {
        return { ok: false, message: 'Management is disabled: the server has no admin key configured.' };
      }

      this.setKey(trimmed);
      return { ok: true, message: 'Administration unlocked.' };

    } catch {
      return { ok: false, message: 'Could not reach the API. It may still be waking up.' };
    }
  }

  signOut() {
    this.key.set(null);
    try { sessionStorage.removeItem(STORAGE_KEY); } catch { /* storage may be blocked */ }
  }

  /**
   * Apollo context for a management mutation. Returns an empty context when not
   * authenticated, though callers should be gating on isAuthenticated anyway.
   */
  context(): Record<string, unknown> {
    const value = this.key();
    return value ? { headers: new HttpHeaders({ 'X-Admin-Key': value }) } : {};
  }

  private setKey(value: string) {
    this.key.set(value);
    try { sessionStorage.setItem(STORAGE_KEY, value); } catch { /* storage may be blocked */ }
  }

  private read(): string | null {
    try { return sessionStorage.getItem(STORAGE_KEY); } catch { return null; }
  }
}

/**
 * Extracts the GraphQL error code from an Apollo result.
 *
 * Apollo Client v4 surfaces GraphQL errors through a combined error object
 * rather than an `errors` array on the result, and the exact shape varies by
 * link configuration, so both are checked.
 */
function firstErrorCode(result: unknown): string | undefined {
  const error = (result as { error?: unknown })?.error as
    | { errors?: Array<{ extensions?: Record<string, unknown> }>;
        graphQLErrors?: Array<{ extensions?: Record<string, unknown> }> }
    | undefined;

  const graphQLErrors = error?.errors ?? error?.graphQLErrors ?? [];

  return graphQLErrors[0]?.extensions?.['code'] as string | undefined;
}
