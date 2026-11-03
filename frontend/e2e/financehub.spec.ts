import { expect, Page, Route, test } from '@playwright/test';

const API_BASE = 'http://127.0.0.1:5099/api';
const TEST_TOKEN =
  'eyJhbGciOiJub25lIn0.eyJlbWFpbCI6ImUyZUBleGFtcGxlLnRlc3QifQ.signature';

type ApiState = {
  members: Array<Record<string, unknown>>;
  categories: Array<Record<string, unknown>>;
  expenses: Array<Record<string, unknown>>;
  requests: Array<{ method: string; path: string; body: unknown }>;
  resetStatus?: number;
};

function initialState(overrides: Partial<ApiState> = {}): ApiState {
  return {
    members: [],
    categories: [],
    expenses: [],
    requests: [],
    ...overrides
  };
}

async function json(route: Route, status: number, body: unknown): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    headers: {
      'access-control-allow-origin': '*',
      'access-control-allow-headers': 'authorization, content-type'
    },
    body: JSON.stringify(body)
  });
}

async function mockApi(page: Page, state: ApiState): Promise<void> {
  await page.route(`${API_BASE}/**`, async route => {
    const request = route.request();
    const method = request.method();
    const path = new URL(request.url()).pathname;
    const body = request.postData() ? request.postDataJSON() : null;

    if (method === 'OPTIONS') {
      await route.fulfill({
        status: 204,
        headers: {
          'access-control-allow-origin': '*',
          'access-control-allow-methods': 'GET, POST, PATCH, DELETE, OPTIONS',
          'access-control-allow-headers': 'authorization, content-type'
        }
      });
      return;
    }

    state.requests.push({ method, path, body });

    if (method === 'POST' && path === '/api/auth/login') {
      await json(route, 200, { accessToken: TEST_TOKEN });
      return;
    }

    if (method === 'POST' && path === '/api/auth/register') {
      await json(route, 200, {
        requiresEmailConfirmation: true,
        message: 'Check your inbox and confirm your email before signing in.'
      });
      return;
    }

    if (method === 'POST' && path === '/api/auth/reset-password') {
      if (state.resetStatus === 429) {
        await json(route, 429, {
          code: 'email_rate_limited',
          message: 'The email service limit has been reached. Please wait before requesting another reset link.',
          retryAfterSeconds: 60
        });
      } else {
        await json(route, 200, {
          message: 'If an account exists, a reset link has been sent.'
        });
      }
      return;
    }

    if (method === 'GET' && path === '/api/family/members') {
      await json(route, 200, state.members);
      return;
    }

    if (method === 'POST' && path === '/api/family') {
      state.members = [{ id: 'member-1', displayName: 'E2E User', role: 'Head' }];
      await json(route, 200, {
        id: 'family-1',
        name: (body as { name: string }).name,
        inviteCode: 'ABCD1234'
      });
      return;
    }

    if (method === 'POST' && path === '/api/family/join') {
      state.members = [{ id: 'member-1', displayName: 'E2E User', role: 'Member' }];
      await json(route, 200, { joined: true });
      return;
    }

    if (method === 'GET' && path === '/api/categories') {
      await json(route, 200, state.categories);
      return;
    }

    if (method === 'GET' && path === '/api/expenses') {
      await json(route, 200, state.expenses);
      return;
    }

    if (method === 'POST' && path === '/api/expenses') {
      const expense = { id: 'expense-1', ...(body as Record<string, unknown>) };
      state.expenses = [expense, ...state.expenses];
      await json(route, 200, expense);
      return;
    }

    if (method === 'GET' && path === '/api/notifications') {
      await json(route, 200, []);
      return;
    }

    await json(route, 404, { message: `Unhandled E2E request: ${method} ${path}` });
  });
}

async function authenticate(page: Page): Promise<void> {
  await page.addInitScript(token => localStorage.setItem('token', token), TEST_TOKEN);
}

test('protected pages redirect unauthenticated users to login', async ({ page }) => {
  await mockApi(page, initialState());

  await page.goto('/dashboard');

  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { name: 'Welcome Back' })).toBeVisible();
});

test('login stores the token and sends a user without a family to onboarding', async ({ page }) => {
  const state = initialState();
  await mockApi(page, state);

  await page.goto('/login');
  await page.locator('input[formcontrolname="email"]').fill('e2e@example.test');
  await page.locator('input[formcontrolname="password"]').fill('correct-horse');
  await page.getByRole('button', { name: 'Log In' }).click();

  await expect(page).toHaveURL(/\/onboarding$/);
  await expect(page.getByRole('heading', { name: 'Get Started' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('token')))
    .toBe(TEST_TOKEN);
  expect(state.requests.some(request =>
    request.method === 'POST' && request.path === '/api/auth/login'
  )).toBeTruthy();
});

test('registration requests the correct confirmation redirect and shows safe feedback', async ({ page }) => {
  const state = initialState();
  await mockApi(page, state);

  await page.goto('/register');
  await page.locator('input[formcontrolname="email"]').fill('new-user@example.test');
  await page.locator('input[formcontrolname="password"]').fill('safe-password');
  await page.getByRole('button', { name: 'Sign Up' }).click();

  await expect(page.getByText('Check your inbox and confirm your email before signing in.')).toBeVisible();
  const request = state.requests.find(item => item.path === '/api/auth/register');
  expect(request?.body).toMatchObject({
    email: 'new-user@example.test',
    redirectTo: 'http://127.0.0.1:4200/login?confirmed=1'
  });
});

test('password recovery displays provider throttling and prevents immediate retries', async ({ page }) => {
  const state = initialState({ resetStatus: 429 });
  await mockApi(page, state);

  await page.goto('/forgot-password');
  await page.locator('input[formcontrolname="email"]').fill('e2e@example.test');
  await page.getByRole('button', { name: 'Send Reset Link' }).click();

  await expect(page.getByText(/email service limit has been reached/i)).toBeVisible();
  await expect(page.getByRole('button', { name: /Try again in \d+s/ })).toBeDisabled();
  expect(state.requests.filter(request => request.path === '/api/auth/reset-password')).toHaveLength(1);
});

test('family head can create a family and continue to the dashboard', async ({ page }) => {
  const state = initialState();
  await authenticate(page);
  await mockApi(page, state);

  await page.goto('/onboarding');
  await page.locator('input[formcontrolname="name"]').fill('E2E Family');
  await page.getByRole('button', { name: 'Create & Invite' }).click();

  await expect(page.getByRole('heading', { name: 'Family Created!' })).toBeVisible();
  await expect(page.getByText('ABCD1234')).toBeVisible();
  await page.getByRole('button', { name: 'Continue to Dashboard' }).click();

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('heading', { name: 'Every expense, clearly in view.' })).toBeVisible();
});

test('authenticated user can save a recurring cash expense and sign out', async ({ page }) => {
  const state = initialState({
    members: [{ id: 'member-1', displayName: 'E2E User', role: 'Head' }],
    categories: [{ id: 'category-food', name: 'Food', color: '#22c55e', isArchived: false }]
  });
  await authenticate(page);
  await mockApi(page, state);

  await page.goto('/dashboard');
  await page.getByRole('button', { name: 'Add expense' }).click();
  await page.locator('select[formcontrolname="categoryId"]').selectOption('category-food');
  await page.locator('input[formcontrolname="amount"]').fill('245.50');
  await page.locator('input[formcontrolname="note"]').fill('E2E grocery purchase');
  await page.locator('#recurringToggle').check();
  await page.getByRole('button', { name: 'Save expense' }).click();

  await expect(page.getByText('E2E grocery purchase')).toBeVisible();
  const createRequest = state.requests.find(request =>
    request.method === 'POST' && request.path === '/api/expenses'
  );
  expect(createRequest?.body).toMatchObject({
    categoryId: 'category-food',
    amount: 245.5,
    note: 'E2E grocery purchase',
    isRecurring: true,
    receiptUrl: null
  });

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect.poll(() => page.evaluate(() => localStorage.getItem('token'))).toBeNull();
});
