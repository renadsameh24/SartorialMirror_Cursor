// MiniJSON.cs (public domain style lightweight JSON parser)
// Source pattern commonly used in Unity projects.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class MiniJSON
{
    public static object Deserialize(string json)
    {
        if (json == null) return null;
        return Parser.Parse(json);
    }

    sealed class Parser : IDisposable
    {
        const string WORD_BREAK = "{}[],:\"";

        public static bool IsWordBreak(char c) => Char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;

        StringReader json;

        Parser(string jsonString) { json = new StringReader(jsonString); }

        public static object Parse(string jsonString)
        {
            using (var instance = new Parser(jsonString))
            {
                return instance.ParseValue();
            }
        }

        public void Dispose() { json.Dispose(); json = null; }

        enum TOKEN { NONE, CURLY_OPEN, CURLY_CLOSE, SQUARE_OPEN, SQUARE_CLOSE, COLON, COMMA, STRING, NUMBER, TRUE, FALSE, NULL }

        TOKEN NextToken
        {
            get
            {
                EatWhitespace();
                if (json.Peek() == -1) return TOKEN.NONE;

                char c = PeekChar;
                switch (c)
                {
                    case '{': json.Read(); return TOKEN.CURLY_OPEN;
                    case '}': json.Read(); return TOKEN.CURLY_CLOSE;
                    case '[': json.Read(); return TOKEN.SQUARE_OPEN;
                    case ']': json.Read(); return TOKEN.SQUARE_CLOSE;
                    case ',': json.Read(); return TOKEN.COMMA;
                    case '"': return TOKEN.STRING;
                    case ':': json.Read(); return TOKEN.COLON;
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                    case '-':
                        return TOKEN.NUMBER;
                }

                string word = NextWord;
                switch (word)
                {
                    case "false": return TOKEN.FALSE;
                    case "true": return TOKEN.TRUE;
                    case "null": return TOKEN.NULL;
                }
                return TOKEN.NONE;
            }
        }

        char PeekChar => Convert.ToChar(json.Peek());
        char NextChar => Convert.ToChar(json.Read());

        string NextWord
        {
            get
            {
                var sb = new StringBuilder();
                while (json.Peek() != -1 && !IsWordBreak(PeekChar))
                    sb.Append(NextChar);
                return sb.ToString();
            }
        }

        void EatWhitespace()
        {
            while (json.Peek() != -1 && Char.IsWhiteSpace(PeekChar))
                json.Read();
        }

        object ParseValue()
        {
            switch (NextToken)
            {
                case TOKEN.STRING: return ParseString();
                case TOKEN.NUMBER: return ParseNumber();
                case TOKEN.CURLY_OPEN: return ParseObject();
                case TOKEN.SQUARE_OPEN: return ParseArray();
                case TOKEN.TRUE: return true;
                case TOKEN.FALSE: return false;
                case TOKEN.NULL: return null;
                default: return null;
            }
        }

        Dictionary<string, object> ParseObject()
        {
            var table = new Dictionary<string, object>();

            // consume '{'
            json.Read();

            while (true)
            {
                switch (NextToken)
                {
                    case TOKEN.NONE: return null;
                    case TOKEN.COMMA: continue;
                    case TOKEN.CURLY_CLOSE: return table;
                    default:
                        string name = ParseString();
                        if (NextToken != TOKEN.COLON) return null;

                        object value = ParseValue();
                        table[name] = value;
                        break;
                }
            }
        }

        List<object> ParseArray()
        {
            var array = new List<object>();

            // consume '['
            json.Read();

            bool parsing = true;
            while (parsing)
            {
                TOKEN token = NextToken;
                switch (token)
                {
                    case TOKEN.NONE: return null;
                    case TOKEN.COMMA: continue;
                    case TOKEN.SQUARE_CLOSE:
                        parsing = false;
                        break;
                    default:
                        object value = ParseValue();
                        array.Add(value);
                        break;
                }
            }
            return array;
        }

        string ParseString()
        {
            var sb = new StringBuilder();
            // consume '"'
            json.Read();

            bool parsing = true;
            while (parsing)
            {
                if (json.Peek() == -1) break;
                char c = NextChar;
                switch (c)
                {
                    case '"':
                        parsing = false;
                        break;
                    case '\\':
                        if (json.Peek() == -1) { parsing = false; break; }
                        c = NextChar;
                        switch (c)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                var hex = new char[4];
                                for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                sb.Append((char)Convert.ToInt32(new string(hex), 16));
                                break;
                        }
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        object ParseNumber()
        {
            string number = NextWord;
            if (number.IndexOf('.') != -1 || number.IndexOf('e') != -1 || number.IndexOf('E') != -1)
            {
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
            }
            return 0;
        }
    }
}
