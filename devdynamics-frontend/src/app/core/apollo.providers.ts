import { Provider, inject } from '@angular/core';
import { InMemoryCache } from '@apollo/client/core';
import { provideApollo } from 'apollo-angular';
import { HttpLink } from 'apollo-angular/http';

import { environment } from '../../environments/environment';

/**
 * GraphQL client, provided at the shell route rather than the application root.
 *
 * Apollo and graphql together are roughly 270 kB — a third of the initial
 * bundle. The landing page issues no queries, so loading them there is pure
 * cost for the visitor most likely to be on a cold connection.
 */
export function provideGraphQL(): Provider {
  return provideApollo(() => {
    const httpLink = inject(HttpLink);

    return {
      link: httpLink.create({ uri: environment.graphqlUri }),
      cache: new InMemoryCache(),
      defaultOptions: {
        query: {
          // Analytics should reflect the current filter state, not a cached
          // response from an earlier one.
          //
          // errorPolicy stays at the default ('none') on purpose: it makes
          // failures throw so each component's catchError renders the error
          // state. Under 'all', a failed request yields undefined data instead,
          // which reads as a TypeError rather than a handled error.
          fetchPolicy: 'network-only'
        }
      }
    };
  });
}
