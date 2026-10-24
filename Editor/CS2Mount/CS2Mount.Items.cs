using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	private const string ItemsGamePath = "scripts/items/items_game.txt";

	private KVObject _itemsGame;
	private readonly object _itemsGameLock = new();

	/// <summary>
	/// scripts/items/items_game.txt's items_game block: items, prefabs, paint kits... (its repeated sections merged), read
	/// once when first asked for. Empty when the game has none.
	/// </summary>
	internal KVObject ItemsGame
	{
		get
		{
			lock ( _itemsGameLock )
			{
				if ( _itemsGame is not null )
					return _itemsGame;

				try
				{
					var text = FileExists( ItemsGamePath ) ? System.Text.Encoding.UTF8.GetString( ReadFile( ItemsGamePath ) ) : "";
					_itemsGame = KV1Reader.Read( text ).GetObject( "items_game" ) ?? new KVObject();
				}
				catch ( InvalidDataException e )
				{
					Log.Warning( $"Couldn't read '{ItemsGamePath}': {e.Message}" );
					_itemsGame = new KVObject();
				}

				return _itemsGame;
			}
		}
	}

	private const string LocalizationPath = "resource/csgo_english.txt";
	private Dictionary<string, string> _localization;

	/// <summary>
	/// A localization token's English text (resource/csgo_english.txt), e.g. <c>#SFUI_WPNHUD_KnifeKaram</c> or
	/// <c>PaintKit_cu_ak47_asiimov_Tag</c>; the token itself when there's none.
	/// </summary>
	internal string Localize( string token )
	{
		if ( string.IsNullOrEmpty( token ) )
			return token;

		lock ( _itemsGameLock )
		{
			if ( _localization is null )
			{
				_localization = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

				try
				{
					if ( FileExists( LocalizationPath ) )
					{
						var bytes = ReadFile( LocalizationPath );
						var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
							? System.Text.Encoding.Unicode.GetString( bytes, 2, bytes.Length - 2 )
							: System.Text.Encoding.UTF8.GetString( bytes );

						var tokens = KV1Reader.Read( text ).GetObject( "lang" )?.GetObject( "Tokens" );
						foreach ( var key in tokens?.Keys ?? [] )
						{
							if ( tokens[key] is string value )
								_localization.TryAdd( key, value );
						}
					}
				}
				catch ( InvalidDataException e )
				{
					Log.Warning( $"Couldn't read '{LocalizationPath}': {e.Message}" );
				}
			}

			return _localization.TryGetValue( token.TrimStart( '#' ), out var localized ) ? localized : token;
		}
	}
}
