using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Reads text KeyValues3, as compiled models keep their key values (<c>m_keyValueText</c>) and loose scripts are
/// written (<c>scripts/surfaceproperties_footsteps.txt</c>). Values come out as <see cref="KVReader"/>'s binary ones do:
/// null, bool, long, double, string, <see cref="KVObject"/> or object[]. Typed values (<c>resource:"..."</c>) keep
/// their string, and flags are dropped.
/// </summary>
internal ref struct KV3TextReader
{
	private readonly ReadOnlySpan<char> _text;
	private int _position;

	private KV3TextReader( ReadOnlySpan<char> text )
	{
		_text = text;
		_position = 0;
	}

	public static KVObject Read( string text )
	{
		var reader = new KV3TextReader( text );
		reader.SkipTrivia();

		return reader.ReadValue() as KVObject ?? throw new InvalidDataException( "KeyValues3 text root is not an object." );
	}

	private object ReadValue()
	{
		SkipTrivia();
		var c = Peek();

		switch ( c )
		{
			case '{': return ReadObject();
			case '[': return ReadArray();
			case '"': return ReadString();
		}

		var word = ReadWord();

		// A flag or type in front of the value (resource:"...", soundevent:"...").
		if ( Peek() == ':' && word.Length > 0 )
		{
			_position++;
			return ReadValue();
		}

		return word switch
		{
			"true" => true,
			"false" => false,
			"null" => null,
			_ when long.TryParse( word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer ) => integer,
			_ when double.TryParse( word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) => number,
			_ => word,
		};
	}

	private KVObject ReadObject()
	{
		_position++;
		var obj = new KVObject();

		while ( true )
		{
			SkipTrivia();
			if ( Peek() == '}' )
			{
				_position++;
				return obj;
			}

			var key = Peek() == '"' ? ReadString() : ReadWord();
			if ( key.Length == 0 )
				throw new InvalidDataException( $"Expected a key at {_position}." );

			SkipTrivia();
			Expect( '=' );
			obj[key] = ReadValue();

			SkipTrivia();
			if ( Peek() == ',' )
				_position++;
		}
	}

	private object[] ReadArray()
	{
		_position++;
		var values = new List<object>();

		while ( true )
		{
			SkipTrivia();
			if ( Peek() == ']' )
			{
				_position++;
				return [.. values];
			}

			values.Add( ReadValue() );

			SkipTrivia();
			if ( Peek() == ',' )
				_position++;
		}
	}

	private string ReadString()
	{
		// Multi-line strings: """ to """, their first and last line breaks not part of the text.
		if ( _text[_position..].StartsWith( "\"\"\"" ) )
		{
			_position += 3;
			var end = _text[_position..].IndexOf( "\"\"\"" );
			if ( end < 0 )
				throw new InvalidDataException( "Unterminated multi-line string." );

			var value = _text.Slice( _position, end ).ToString();
			_position += end + 3;
			return value.Trim( '\r', '\n' );
		}

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
				builder.Append( escaped switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => escaped } );
				continue;
			}

			builder.Append( c );
		}

		throw new InvalidDataException( "Unterminated string." );
	}

	private string ReadWord()
	{
		var start = _position;
		while ( _position < _text.Length && !char.IsWhiteSpace( _text[_position] ) && _text[_position] is not ('=' or ',' or ':' or '{' or '}' or '[' or ']' or '"') )
			_position++;

		return _text[start.._position].ToString();
	}

	private void SkipTrivia()
	{
		while ( _position < _text.Length )
		{
			var rest = _text[_position..];

			if ( char.IsWhiteSpace( rest[0] ) )
				_position++;
			else if ( rest.StartsWith( "//" ) )
				_position += rest.IndexOf( '\n' ) is var end and >= 0 ? end + 1 : rest.Length;
			else if ( rest.StartsWith( "/*" ) )
				_position += rest.IndexOf( "*/" ) is var end and >= 0 ? end + 2 : rest.Length;
			else if ( rest.StartsWith( "<!--" ) )
				_position += rest.IndexOf( "-->" ) is var end and >= 0 ? end + 3 : rest.Length;
			else
				break;
		}
	}

	private char Peek() => _position < _text.Length ? _text[_position] : '\0';

	private void Expect( char c )
	{
		if ( Peek() != c )
			throw new InvalidDataException( $"Expected '{c}' at {_position}, found '{Peek()}'." );

		_position++;
	}
}
