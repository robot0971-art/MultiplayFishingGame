create table if not exists public.player_equipment (
    user_id uuid primary key default auth.uid() references auth.users(id) on delete cascade,
    owned_rod_ids text[] not null default array['rod_basic']::text[],
    owned_bait_ids text[] not null default array[]::text[],
    equipped_rod_id text not null default '',
    equipped_bait_id text not null default '',
    updated_at timestamptz not null default now()
);

alter table public.player_equipment enable row level security;

drop policy if exists "Players can read own equipment." on public.player_equipment;
create policy "Players can read own equipment."
on public.player_equipment
for select
to authenticated
using ((select auth.uid()) = user_id);

drop policy if exists "Players can insert own equipment." on public.player_equipment;
create policy "Players can insert own equipment."
on public.player_equipment
for insert
to authenticated
with check ((select auth.uid()) = user_id);

drop policy if exists "Players can update own equipment." on public.player_equipment;
create policy "Players can update own equipment."
on public.player_equipment
for update
to authenticated
using ((select auth.uid()) = user_id)
with check ((select auth.uid()) = user_id);
