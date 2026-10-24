using System;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Reads text KeyValues (KeyValues1: <c>"key" "value"</c> and <c>"key" { ... }</c>), as scripts/items/items_game.txt is
/// written. Values are strings and blocks are <see cref="KVObject"/>s. A block whose key repeats is merged into the first
/// (items_game splits sections like paint_kits and items over several blocks), its keys replacing the earlier ones; a
/// repeated plain value replaces the earlier one. Comments (//) and platform conditionals ([$WIN32]) are skipped.
/// </summary>
internal ref struct KV1Reader
{
	private readonly ReadOnlySpan<char> _text;
	private int _position;

	private KV1Reader( ReadOnlySpan<char> text )
	{
		_text = text;
		_position = 0;
	}

	public static KVObject Read( string text )
	{
		var reader = new KV1Reader( text );
		var root = new KVObject();
		reader.ReadInto( root, topLevel: true );
		return root;
	}

	private void ReadInto( KVObject obj, bool topLevel )
	{
		while ( true )
		{
			SkipTrivia();
			if ( _position >= _text.Length )
			{
				if ( !topLevel )
					throw new InvalidDataException( "Unterminated block." );

				return;
			}

			if ( Peek() == '}' )
			{
				if ( topLevel )
					throw new InvalidDataException( $"Unexpected '}}' at {_position}." );

				_position++;
				return;
			}

			var key = ReadToken();
			SkipTrivia();
			SkipConditional();

			if ( Peek() == '{' )
			{
				_position++;
				if ( obj[key] is not KVObject block )
					obj[key] = block = new KVObject();

				ReadInto( block, topLevel: false );
			}
			else
			{
				obj[key] = ReadToken();
			}

			SkipTrivia();
			SkipConditional();
		}
	}

	private string ReadToken()
	{
		if ( Peek() == '"' )
		{
			_position++;
			var builder = new StringBuilder();

			while ( _position < _text.Length )
			{
				var c = _text[_position++];
				if ( c == '"' )
					return builder.ToString();

				if ( c == '\\' && _position < _text.Length )
				{
					var escaped = _text[_position++];
					builder.Append( escaped switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => escaped } );
					continue;
				}

				builder.Append( c );
			}

			throw new InvalidDataException( "Unterminated string." );
		}

		var start = _position;
		while ( _position < _text.Length && !char.IsWhiteSpace( _text[_position] ) && _text[_position] is not ('{' or '}' or '"') )
			_position++;

		if ( start == _position )
			throw new InvalidDataException( $"Expected a key or value at {_position}." );

		return _text[start.._position].ToString();
	}

	private void SkipConditional()
	{
		if ( Peek() != '[' )
			return;

		var end = _text[_position..].IndexOf( ']' );
		_position += end >= 0 ? end + 1 : _text.Length - _position;
		SkipTrivia();
	}

	private void SkipTrivia()
	{
		while ( _position < _text.Length )
		{
			var rest = _text[_position..];
			if ( char.IsWhiteSpace( rest[0] ) || rest[0] == '﻿' )
				_position++;
			else if ( rest.StartsWith( "//" ) )
				_position += rest.IndexOf( '\n' ) is var end and >= 0 ? end + 1 : rest.Length;
			else
				break;
		}
	}

	private char Peek() => _position < _text.Length ? _text[_position] : '\0';
}
