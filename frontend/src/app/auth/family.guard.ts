import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { FamilyService } from '../family.service';
import { map, catchError, of } from 'rxjs';

export const familyGuard = () => {
  const familyService = inject(FamilyService);
  const router = inject(Router);

  return familyService.getMembers().pipe(
    map(members => {
      if (members && members.length > 0) {
        return true;
      } else {
        router.navigate(['/onboarding']);
        return false;
      }
    }),
    catchError(() => {
      router.navigate(['/onboarding']);
      return of(false);
    })
  );
};
