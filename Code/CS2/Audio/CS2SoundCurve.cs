using System;

namespace CS2;

/// <summary>
/// A sound event's mapping curve (e.g. distance_volume_mapping_curve): points of [x, y, tangent in, tangent out, tangent
/// type in, tangent type out], evaluated as a cubic Hermite spline between them. A port of ValveResourceFormat's
/// SoundEventCurve.
/// </summary>
public sealed class CS2SoundCurve
{
	private enum TangentType
	{
		Linear = 0,
		Spline = 1,
		Free = 2,
		Mirror = 3,
		Sine = 4,
		Flat = 5,
	}

	private struct Knot
	{
		public float X;
		public float Y;
		public float InTangent;
		public float OutTangent;
	}

	private const float XEpsilon = 0.0001f;
	private const float SineSteep = 1.6030499935150146f;
	private const float SineShallow = 0.04133769869804382f;

	private readonly Knot[] points;
	private readonly float outputMin;
	private readonly float outputMax;

	/// <summary>
	/// The largest x the curve covers.
	/// </summary>
	public float MaxX => points[^1].X;

	/// <summary>
	/// Whether it falls off at all; a flat curve is a constant gain left in the data, not a distance falloff.
	/// </summary>
	public bool Attenuates => points[^1].Y < points[0].Y - 0.0001f;

	private CS2SoundCurve( Knot[] points )
	{
		this.points = points;
		outputMin = points.Min( x => x.Y );
		outputMax = points.Max( x => x.Y );
	}

	/// <summary>
	/// A curve from its points, each [x, y] optionally followed by the tangents and their types; null without any.
	/// </summary>
	public static CS2SoundCurve FromPoints( IEnumerable<float[]> points )
	{
		var authored = new List<(Knot Knot, TangentType In, TangentType Out, int Order)>();
		foreach ( var point in points )
		{
			if ( point is not { Length: >= 2 } )
				continue;

			authored.Add( (new Knot
			{
				X = point[0],
				Y = point[1],
				InTangent = point.Length > 2 ? point[2] : 0f,
				OutTangent = point.Length > 3 ? point[3] : 0f,
			}, point.Length > 4 ? (TangentType)(int)point[4] : TangentType.Linear,
			point.Length > 5 ? (TangentType)(int)point[5] : TangentType.Linear,
			authored.Count) );
		}

		if ( authored.Count == 0 )
			return null;

		// Sorted by x, the later of two points at the same x replacing the earlier.
		var sorted = authored.OrderBy( x => x.Knot.X ).ThenBy( x => x.Order ).ToList();
		var kept = new List<(Knot Knot, TangentType In, TangentType Out, int Order)>();
		foreach ( var point in sorted )
		{
			if ( kept.Count > 0 && point.Knot.X == kept[^1].Knot.X )
				kept.RemoveAt( kept.Count - 1 );

			kept.Add( point );
		}

		var knots = kept.Select( x => x.Knot ).ToArray();
		for ( var i = 1; i < knots.Length; i++ )
			knots[i].X = MathF.Max( knots[i].X, knots[i - 1].X + XEpsilon );

		if ( knots.Length > 1 )
			ResolveTangents( knots, kept.Select( x => (x.In, x.Out) ).ToArray() );

		return new CS2SoundCurve( knots );
	}

	private static void ResolveTangents( Knot[] knots, (TangentType In, TangentType Out)[] types )
	{
		for ( var i = 0; i < knots.Length; i++ )
		{
			var knot = knots[i];
			var (typeIn, typeOut) = types[i];
			var hasPrevious = i > 0;
			var hasNext = i + 1 < knots.Length;
			var previous = hasPrevious ? knots[i - 1] : knot;
			var next = hasNext ? knots[i + 1] : knot;
			var previousSlope = hasPrevious ? Slope( previous, knot ) : 0f;
			var nextSlope = hasNext ? Slope( knot, next ) : 0f;
			var spanSlope = hasPrevious && hasNext ? Slope( previous, next ) : hasPrevious ? previousSlope : nextSlope;

			var inTangent = typeIn switch
			{
				TangentType.Linear => previousSlope,
				TangentType.Spline => spanSlope,
				TangentType.Mirror => 0f,
				TangentType.Sine => SineTangent( knot.Y > previous.Y ? -SineShallow : -SineSteep, knot.X - previous.X ),
				TangentType.Flat => 0f,
				_ => knot.InTangent,
			};

			var outTangent = typeOut switch
			{
				TangentType.Linear => nextSlope,
				TangentType.Spline => spanSlope,
				TangentType.Mirror => inTangent,
				TangentType.Sine => SineTangent( next.Y > knot.Y ? SineSteep : SineShallow, next.X - knot.X ),
				TangentType.Flat => 0f,
				_ => knot.OutTangent,
			};

			knots[i].InTangent = typeIn == TangentType.Mirror ? outTangent : inTangent;
			knots[i].OutTangent = outTangent;
		}
	}

	private static float Slope( Knot from, Knot to ) => (to.Y - from.Y) / (to.X - from.X);

	private static float SineTangent( float slope, float width ) => width != 0f ? slope / width : slope;

	/// <summary>
	/// The curve at x, held at its first and last points outside them.
	/// </summary>
	public float Evaluate( float x )
	{
		if ( x <= points[0].X )
			return points[0].Y;

		if ( x >= points[^1].X )
			return points[^1].Y;

		var low = 1;
		var high = points.Length - 1;
		while ( low < high )
		{
			var middle = (low + high) >> 1;
			if ( x > points[middle].X )
				low = middle + 1;
			else
				high = middle;
		}

		var left = points[low - 1];
		var right = points[low];
		var width = right.X - left.X;
		var t = (x - left.X) / width;
		var t2 = t * t;
		var t3 = t2 * t;

		var value = (2f * t3 - 3f * t2 + 1f) * left.Y
			+ (t3 - 2f * t2 + t) * width * left.OutTangent
			+ (-2f * t3 + 3f * t2) * right.Y
			+ (t3 - t2) * width * right.InTangent;

		return Math.Clamp( value, outputMin, outputMax );
	}
}
