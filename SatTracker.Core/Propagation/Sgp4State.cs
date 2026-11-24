using SatTracker.Core.Vectors;

namespace SatTracker.Core.Propagation
{
    /// <summary>
    /// Where a satellite is and how fast it's moving at one moment, as SGP4 predicts it.
    /// <para>
    /// <b>Frame: TEME</b> ("True Equator, Mean Equinox"). The origin is Earth's center and the axes
    /// do NOT spin with Earth: Z points along Earth's spin axis (north), X points towards a fixed
    /// direction among the stars (roughly where the Sun is at the March equinox), Y completes the
    /// set. It's SGP4's own "space" frame. To put the satellite over a city on a spinning globe,
    /// it still has to be rotated by Earth's spin angle at that moment (a later step).
    /// </para>
    /// </summary>
    public readonly struct Sgp4State
    {
        public Sgp4State(double minutesSinceEpoch, Vector3d positionKm, Vector3d velocityKmPerSec)
        {
            MinutesSinceEpoch = minutesSinceEpoch;
            PositionKm = positionKm;
            VelocityKmPerSec = velocityKmPerSec;
        }

        /// <summary>The time this state is for, in minutes after the snapshot's epoch.</summary>
        public double MinutesSinceEpoch { get; }

        /// <summary>Position from Earth's center, TEME frame, km.</summary>
        public Vector3d PositionKm { get; }

        /// <summary>Velocity, TEME frame, km per second.</summary>
        public Vector3d VelocityKmPerSec { get; }
    }
}
