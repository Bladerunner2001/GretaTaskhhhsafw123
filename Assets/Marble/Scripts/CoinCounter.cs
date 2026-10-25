using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks every Coin in the loaded scenes. No scene object needed: coins register themselves,
/// and UI listens to <see cref="Changed"/>.
/// </summary>
public static class CoinCounter
{
    static readonly HashSet<Coin> coins = new HashSet<Coin>();

    public static int CollectedCoins { get; private set; }
    public static int TotalCoins => coins.Count;
    public static int Score { get; private set; }

    public static event System.Action Changed;
    public static event System.Action AllCollected;

    // Static state survives play mode restarts when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        coins.Clear();
        CollectedCoins = 0;
        Score = 0;
        Changed = null;
        AllCollected = null;
    }

    internal static void Register(Coin coin)
    {
        if (coins.Add(coin))
            Changed?.Invoke();
    }

    internal static void Unregister(Coin coin)
    {
        if (!coins.Remove(coin))
            return;

        if (coin.IsCollected)
        {
            CollectedCoins--;
            Score -= coin.value;
        }
        Changed?.Invoke();
    }

    internal static void NotifyCollected(Coin coin)
    {
        CollectedCoins++;
        Score += coin.value;
        Changed?.Invoke();

        if (CollectedCoins == TotalCoins)
            AllCollected?.Invoke();
    }
}
