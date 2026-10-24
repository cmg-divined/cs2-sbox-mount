using System.IO;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a post processing resource's colour correction lookup table (vpost_c) as a 3D texture, RGBA8 and read without
/// sRGB decoding as the game's post_process.vfx reads it.
/// </summary>
internal sealed class ColorCorrectionLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var post = new CompiledPostProcessing( Host.ReadFile( filePath ) );
		if ( post.ColorCorrectionSize == 0 )
			throw new InvalidDataException( "Post processing has no colour correction." );

		var size = post.ColorCorrectionSize;
		return Texture.CreateVolume( size, size, size, ImageFormat.RGBA8888 )
			.WithName( Path )
			.WithData( post.ColorCorrection )
			.Finish();
	}
}
