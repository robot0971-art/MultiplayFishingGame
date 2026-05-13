# Supabase Caught Fish Pilot

This pilot stores a successful catch in Supabase without changing the local save flow.

## Supabase Setup

1. Create or open a Supabase project.
2. Enable Auth. For a quick test, enable Anonymous Sign-ins in the Supabase dashboard.
3. Run the SQL in `supabase/migrations/0001_caught_fish_pilot.sql`.
4. In Unity, create a config asset:
   - `Assets/Create/Fishing/Supabase Project Config`
   - Set `Project Url` to `https://<project-ref>.supabase.co`
   - Set `Publishable Key` to the project's publishable or anon key
   - Set `Access Token` to an authenticated user's JWT for the first pilot
5. Add `SupabaseCaughtFishSyncService` to one object in the Lobby or Play scene and assign the config asset.

## Runtime Flow

When a local player successfully catches a fish:

1. The existing `IUserService.AddFish` local save still runs.
2. `FishingPlayer` tries to resolve `ICaughtFishSyncService`.
3. If Supabase is configured, it inserts into `public.caught_fish`.
4. If Supabase is not configured, the game continues with only local save.

## Security Notes

- Do not put a `service_role` key in Unity.
- RLS is enabled on both pilot tables.
- `caught_fish.user_id` defaults to `auth.uid()`.
- Insert/select policies only allow authenticated users to access their own rows.

## Next Step

Replace the temporary pasted `Access Token` with a real Unity login flow. Anonymous Auth is the quickest next step.
