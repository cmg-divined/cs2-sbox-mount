using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a compiled CS2 texture (vtex_c) as an s&amp;box <see cref="Texture"/>. Block compressed data is
/// uploaded as it is; the mip chain is already in the order the texture builders take.
/// </summary>
/// <remarks>
/// A loader can take part of the mip chain: <paramref name="mipCount"/> mips from <paramref name="firstMip"/>. The mount
/// serves the maps' 8192² lightmaps that way, split in two (see <see cref="CS2Mount.LightmapTopMip"/>).
/// </remarks>
internal sealed class TextureLoader( string filePath, int firstMip = 0, int mipCount = int.MaxValue ) : ResourceLoader<CS2Mount>
{
	/// <summary>
	/// The most data a texture is created with. Creating dust2's 80 MiB lightmaps (8192² BC6H and BC7 with a second
	/// mip) faulted the GPU's copy engine twice (2026-10-20); the Deadlock mount's 64 MiB lightmaps never did. Larger
	/// textures fail to load instead.
	/// </summary>
	internal const long MaxUploadBytes = 64L * 1024 * 1024;

	protected override object Load()
	{
		var data = Host.ReadFile( filePath );
		var texture = new CompiledTexture( data );

		return texture.IsRawImage
			? CreateFromImage( texture )
			: Create( texture, Path, firstMip, mipCount );
	}

	private static Texture CreateFromImage( CompiledTexture texture )
	{
		var bitmap = Bitmap.CreateFromBytes( texture.ReadRawImage() )
			?? throw new InvalidDataException( "Texture image data could not be decoded." );

		return bitmap.ToTexture( true );
	}

	/// <summary>
	/// Creates a texture from <paramref name="mipCount"/> mips of a compiled one, leaving out its
	/// <paramref name="firstMip"/> largest mips.
	/// </summary>
	internal static Texture Create( CompiledTexture texture, string name, int firstMip = 0, int mipCount = int.MaxValue )
	{
		var format = GetImageFormat( texture.Format );
		if ( format == ImageFormat.None )
			throw new NotSupportedException( $"Texture format {texture.Format} is not supported." );

		if ( texture.Width > short.MaxValue || texture.Height > short.MaxValue )
			throw new InvalidDataException( $"Texture is too large ({texture.Width}x{texture.Height})." );

		firstMip = Math.Clamp( firstMip, 0, texture.MipCount - 1 );
		var width = Math.Max( texture.Width >> firstMip, 1 );
		var height = Math.Max( texture.Height >> firstMip, 1 );
		var mips = Math.Clamp( mipCount, 1, texture.MipCount - firstMip );

		long size = 0;
		for ( var mip = firstMip; mip < firstMip + mips; mip++ )
			size += texture.GetMipSize( mip );

		if ( size > MaxUploadBytes )
			throw new NotSupportedException( $"Texture is {size / (1024 * 1024)} MiB, more than the {MaxUploadBytes / (1024 * 1024)} MiB one texture is created with." );

		if ( texture.IsCube )
		{
			// Each mip holds every face of every cube in turn, the order cube arrays are laid out in.
			var cube = new TextureCubeBuilder()
				.WithName( name )
				.WithFormat( format )
				.WithSize( width, height )
				.WithData( texture.ReadMips( firstMip, mips ) )
				.WithMips( mips )
				.WithStaticUsage();

			if ( texture.Depth > 1 )
				cube = cube.WithArrayCount( texture.Depth );

			return cube.Finish();
		}

		var data = texture.ReadMips( firstMip, mips );

		if ( texture.IsVolume )
		{
			return new Texture3DBuilder()
				.WithName( name )
				.WithFormat( format )
				.WithSize( width, height, Math.Max( texture.Depth >> firstMip, 1 ) )
				.WithData( data )
				.WithMips( mips )
				.WithStaticUsage()
				.Finish();
		}

		if ( texture.Depth > 1 )
		{
			return new TextureArrayBuilder()
				.WithName( name )
				.WithFormat( format )
				.WithSize( width, height )
				.WithCount( texture.Depth )
				.WithData( data )
				.WithMips( mips )
				.WithStaticUsage()
				.Finish();
		}

		return new Texture2DBuilder()
			.WithName( name )
			.WithFormat( format )
			.WithSize( width, height )
			.WithData( data )
			.WithMips( mips )
			.WithStaticUsage()
			.Finish();
	}

	private static ImageFormat GetImageFormat( VTexFormat format ) => format switch
	{
		VTexFormat.DXT1 => ImageFormat.DXT1,
		VTexFormat.DXT5 => ImageFormat.DXT5,
		VTexFormat.ATI1N => ImageFormat.ATI1N,
		VTexFormat.ATI2N => ImageFormat.ATI2N,
		VTexFormat.BC6H => ImageFormat.BC6H,
		VTexFormat.BC7 => ImageFormat.BC7,
		VTexFormat.RGBA8888 => ImageFormat.RGBA8888,
		VTexFormat.BGRA8888 => ImageFormat.BGRA8888,
		VTexFormat.I8 or VTexFormat.R8_UNORM => ImageFormat.I8,
		VTexFormat.A8 => ImageFormat.A8,
		VTexFormat.IA88 => ImageFormat.IA88,
		VTexFormat.R16 => ImageFormat.R16,
		VTexFormat.RG1616 => ImageFormat.RG1616,
		VTexFormat.RGBA16161616 => ImageFormat.RGBA16161616,
		VTexFormat.R16F => ImageFormat.R16F,
		VTexFormat.RG1616F => ImageFormat.RG1616F,
		VTexFormat.RGBA16161616F => ImageFormat.RGBA16161616F,
		VTexFormat.R32F => ImageFormat.R32F,
		VTexFormat.RG3232F => ImageFormat.RG3232F,
		VTexFormat.RGB323232F => ImageFormat.RGB323232F,
		VTexFormat.RGBA32323232F => ImageFormat.RGBA32323232F,
		VTexFormat.R32_UINT => ImageFormat.R32_UINT,
		VTexFormat.ETC2 => ImageFormat.R8G8B8_ETC2,
		VTexFormat.ETC2_EAC => ImageFormat.R8G8B8A8_ETC2_EAC,
		VTexFormat.R11_EAC => ImageFormat.R11_EAC,
		VTexFormat.RG11_EAC => ImageFormat.RG11_EAC,
		_ => ImageFormat.None,
	};
}
