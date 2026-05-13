alter table public.caught_fish
add column if not exists sold_at timestamptz,
add column if not exists sold_price integer;

create index if not exists caught_fish_user_unsold_idx
on public.caught_fish (user_id, caught_at desc)
where sold_at is null;

drop policy if exists "Players can update own caught fish." on public.caught_fish;
create policy "Players can update own caught fish."
on public.caught_fish
for update
to authenticated
using ((select auth.uid()) = user_id)
with check ((select auth.uid()) = user_id);
