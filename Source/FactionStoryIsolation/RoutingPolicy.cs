namespace Meow.FactionStoryIsolation
{
    internal static class RoutingPolicy
    {
        internal static bool AllowsOwnedTarget(int ownerId, bool ownerIsPlayer, int targetFactionId)
            => ownerIsPlayer && ownerId >= 0 && ownerId == targetFactionId;
    }
}
