alter table public.profiles
alter column user_id set default auth.uid();

update public.profiles
set updated_at = now()
where updated_at is null;
