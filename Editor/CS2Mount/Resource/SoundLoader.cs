using System;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a compiled CS2 sound (vsnd_c) as a <see cref="SoundFile"/>. The audio itself follows the resource in the file,
/// from the resource's own size on, as the game streams it; the resource describes it: in its CTRL block (resource
/// version 5) or in its DATA block's fixed layout (version 4).
/// </summary>
internal sealed class SoundLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	/// <summary>
	/// The audio formats of version 4 sounds, as their DATA block numbers them.
	/// </summary>
	private enum FormatV4 : byte
	{
		Pcm16 = 0,
		Pcm8 = 1,
		Mp3 = 2,
		Adpcm = 3,
	}

	private readonly record struct SoundInfo( string Format, int Rate, int Channels, int LoopStart, int LoopEnd, int StreamingSize );

	protected override object Load()
	{
		var data = Host.ReadFile( filePath );
		var resource = new ResourceFile( data );
		var info = ReadControl( resource ) ?? ReadDataV4( resource );

		var start = (int)BinaryPrimitives.ReadUInt32LittleEndian( data );
		var length = info.StreamingSize > 0 ? Math.Min( info.StreamingSize, data.Length - start ) : data.Length - start;
		if ( start <= 0 || length <= 0 )
			throw new InvalidDataException( "Sound has no audio data." );

		var audio = data.AsSpan( start, length );

		return info.Format switch
		{
			"MP3" => SoundFile.FromMp3( Path, audio, new SoundFile.LoadOptions { LoopStart = info.LoopStart, LoopEnd = info.LoopEnd } ),
			"PCM16" or "PCM8" => SoundFile.FromPcm( Path, audio, new SoundFile.PcmOptions
			{
				Channels = info.Channels,
				Rate = (uint)info.Rate,
				Bits = info.Format == "PCM16" ? 16 : 8,
				LoopStart = info.LoopStart,
				LoopEnd = info.LoopEnd,
			} ),
			_ => throw new NotSupportedException( $"Sound format {info.Format} isn't supported." ),
		};
	}

	/// <summary>
	/// Version 5 sounds describe themselves in CTRL (a CVoiceContainerDefault or CVoiceContainerEnvelope's m_vSound),
	/// leaving DATA empty.
	/// </summary>
	private static SoundInfo? ReadControl( ResourceFile resource )
	{
		if ( resource.FindBlock( "CTRL" ) is null || resource.ReadKeyValues( "CTRL" ) is not { } control )
			return null;

		if ( control.GetString( "_class" ) is not ("CVoiceContainerDefault" or "CVoiceContainerEnvelope") || control.GetObject( "m_vSound" ) is not { } sound )
			return null;

		return new SoundInfo(
			sound["m_nFormat"] switch
			{
				string name => name,
				{ } value => ((FormatV4)KVValue.ToInt64( value )).ToString().ToUpperInvariant(),
				_ => "",
			},
			sound.GetInt32( "m_nRate" ),
			sound.GetInt32( "m_nChannels", 1 ),
			sound.GetInt32( "m_nLoopStart", -1 ),
			sound.GetInt32( "m_nLoopEnd" ),
			sound.GetInt32( "m_nStreamingSize" ) );
	}

	/// <summary>
	/// Version 4 sounds' DATA: rate, format, channels, loop start, sample count, duration, the sentence and header
	/// pointers, the streaming size, then fields later versions added (the loop end last, from version 4).
	/// </summary>
	private static SoundInfo ReadDataV4( ResourceFile resource )
	{
		if ( resource.Version != 4 || resource.FindBlock( "DATA" ) is not { } block )
			throw new NotSupportedException( $"Sound resource version {resource.Version} isn't supported." );

		var data = resource.GetBlockData( block );
		var rate = BinaryPrimitives.ReadUInt16LittleEndian( data );
		var format = (FormatV4)data[2];
		var channels = data[3];
		var loopStart = BinaryPrimitives.ReadInt32LittleEndian( data[4..] );
		// 8: sample count, 12: duration, 16: sentence offset, 20: header offset, 24: header size.
		var streamingSize = (int)BinaryPrimitives.ReadUInt32LittleEndian( data[28..] );
		// 32 and 36: version 1's offset and count, 40: version 2's value, 44: version 4's loop end.
		var loopEnd = data.Length >= 48 ? BinaryPrimitives.ReadInt32LittleEndian( data[44..] ) : 0;

		return new SoundInfo( format.ToString().ToUpperInvariant(), rate, channels, loopStart, loopEnd, streamingSize );
	}
}
