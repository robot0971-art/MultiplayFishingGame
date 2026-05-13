create table if not exists public.wallets (
    user_id uuid primary key default auth.uid() references auth.users(id) on delete cascade,
    gold integer not null default 0 check (gold >= 0),
    updated_at timestamptz not null default now()
);

alter table public.wallets enable row level security;

drop policy if exists "Players can read own wallet." on public.wallets;
create policy "Players can read own wallet."
on public.wallets
for select
to authenticated
using ((select auth.uid()) = user_id);

drop policy if exists "Players can insert own wallet." on public.wallets;
create policy "Players can insert own wallet."
on public.wallets
for insert
to authenticated
with check ((select auth.uid()) = user_id);

drop policy if exists "Players can update own wallet." on public.wallets;
create policy "Players can update own wallet."
on public.wallets
for update
to authenticated
using ((select auth.uid()) = user_id)
with check ((select auth.uid()) = user_id);
