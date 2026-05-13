create table if not exists public.profiles (
    user_id uuid primary key references auth.users(id) on delete cascade,
    player_name text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table public.profiles enable row level security;

drop policy if exists "Players can read own profile." on public.profiles;
create policy "Players can read own profile."
on public.profiles
for select
to authenticated
using ((select auth.uid()) = user_id);

drop policy if exists "Players can insert own profile." on public.profiles;
create policy "Players can insert own profile."
on public.profiles
for insert
to authenticated
with check ((select auth.uid()) = user_id);

drop policy if exists "Players can update own profile." on public.profiles;
create policy "Players can update own profile."
on public.profiles
for update
to authenticated
using ((select auth.uid()) = user_id)
with check ((select auth.uid()) = user_id);

create table if not exists public.caught_fish (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null default auth.uid() references auth.users(id) on delete cascade,
    player_name text,
    fish_id text not null,
    fish_name text,
    rank text,
    length_cm numeric(7, 2) not null check (length_cm > 0),
    exp_reward integer not null default 0,
    sell_price integer not null default 0,
    caught_at timestamptz not null default now()
);

create index if not exists caught_fish_user_caught_at_idx
on public.caught_fish (user_id, caught_at desc);

create index if not exists caught_fish_user_fish_idx
on public.caught_fish (user_id, fish_id);

alter table public.caught_fish enable row level security;

drop policy if exists "Players can read own caught fish." on public.caught_fish;
create policy "Players can read own caught fish."
on public.caught_fish
for select
to authenticated
using ((select auth.uid()) = user_id);

drop policy if exists "Players can insert own caught fish." on public.caught_fish;
create policy "Players can insert own caught fish."
on public.caught_fish
for insert
to authenticated
with check ((select auth.uid()) = user_id);
