namespace Game.Entities;

/// <summary>
/// Supported power-up behaviors that can be dropped and collected during a run.
/// </summary>
public enum PowerUpType
{
    /// <summary>Reduces shot cooldown for faster firing.</summary>
    RapidFire,
    /// <summary>Fires three bullets in a spread pattern.</summary>
    MultiShot,
    /// <summary>Bullets can pass through and damage multiple bars.</summary>
    PiercingShot,
    /// <summary>Absorbs one floor-hit life loss.</summary>
    Shield,
    /// <summary>Temporarily slows effective bar speed.</summary>
    SlowMotion,
    /// <summary>Triggers delayed bomb explosion behavior.</summary>
    BombShot
}
