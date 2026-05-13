# Supabase Caught Fish Pilot

This pilot stores a successful catch in Supabase without changing the local save flow.

## Supabase Setup

1. Create or open a Supabase project.
2. Enable Auth. For this pilot, enable Anonymous Sign-ins in the Supabase dashboard.
3. Run the SQL migrations in order:
   - `supabase/migrations/0001_caught_fish_pilot.sql`
   - `supabase/migrations/0002_caught_fish_sold_state.sql`
   - `supabase/migrations/0003_wallets.sql`
   - `supabase/migrations/0004_profile_user_id_default.sql`
4. In Unity, create a config asset:
   - `Assets/Create/Fishing/Supabase Project Config`
   - Set `Project Url` to `https://<project-ref>.supabase.co`
   - Set `Publishable Key` to the project's publishable or anon key
5. Add `SupabaseCaughtFishSyncService` to one object in the Lobby or Play scene and assign the config asset.

## Runtime Flow

When a local player successfully catches a fish:

1. The existing `IUserService.AddFish` local save still runs.
2. `FishingPlayer` tries to resolve `ICaughtFishSyncService`.
3. If Supabase is configured, it signs in anonymously, stores the session in `PlayerPrefs`, then inserts into `public.caught_fish`.
4. If Supabase is not configured, the game continues with only local save.

On startup, `SupabaseCaughtFishSyncService` also reads the current user's `caught_fish` rows and merges missing fish into the local `UserData` cache. Existing UI can keep reading `userService.UserData`.
Sold fish are marked with `sold_at` and `sold_price` in Supabase, then excluded from future local cache syncs.
The same service also syncs the current user's gold through `public.wallets`. Startup pulls the remote wallet into local `UserData`, and selling fish or buying shop items upserts the latest gold value.
The local player's display name is synced through `public.profiles`. Startup prefers the remote `player_name`; if no profile exists yet, the current local `PlayerPrefs` name is uploaded as the initial profile name.

## Security Notes

- Do not put a `service_role` key in Unity.
- RLS is enabled on all pilot tables.
- `caught_fish.user_id` defaults to `auth.uid()`.
- `wallets.user_id` defaults to `auth.uid()`.
- `profiles.user_id` defaults to `auth.uid()`.
- Insert/select/update policies only allow authenticated users to access their own rows.

## Notes

`Access Token` is kept on the config asset only as a temporary fallback field. The pilot now signs in anonymously at runtime and refreshes the session with the saved refresh token.

The local `UserData.json` file is still used as a cache. Supabase is the remote source for caught fish history, but the current inventory/encyclopedia UI stays unchanged.

Run `supabase/migrations/0003_wallets.sql` before testing gold sync. After selling a fish, check the `wallets` table: the row for the anonymous user should show the updated `gold` value.
Run `supabase/migrations/0004_profile_user_id_default.sql` before testing profile sync. After entering the play scene, check the `profiles` table: the row for the anonymous user should show `player_name`.
