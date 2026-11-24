using System;
using System.Globalization;

namespace SatTracker.Core.Vectors
{
    /// <summary>
    /// Three doubles: X, Y, Z. Used for positions and velocities in space.
    /// <para>
    /// Not <c>UnityEngine.Vector3</c> (that's float, about 7 digits, so it can't hold a position
    /// to the millimetre at 7,000 km) and not <c>System.Numerics.Vector3</c> (also float).
    /// In Unity, turn this into a <c>Vector3</c> only at the very end, for drawing, after
    /// scaling it down to scene units.
    /// </para>
    /// The vector itself has no unit: the property holding it says what the unit is,
    /// e.g. <c>PositionKm</c> or <c>VelocityKmPerSec</c>.
    /// </summary>
    public readonly struct Vector3d : IEquatable<Vector3d>
    {
        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        /// <summary>Length of the arrow: √(X² + Y² + Z²), in the same unit as the vector.</summary>
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public bool Equals(Vector3d other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Vector3d other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public static bool operator ==(Vector3d a, Vector3d b) => a.Equals(b);

        public static bool operator !=(Vector3d a, Vector3d b) => !a.Equals(b);

        /// <summary>Full precision ("R" format), so printing never hides digits.</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", X, Y, Z);
        }
    }
}
