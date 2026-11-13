using System.Text;

namespace Radial.Core.Model;

/// <summary>
/// Divide una línea de argumentos escrita como TEXTO (el formato antiguo de <c>arguments</c> en <c>config.json</c>) en la lista que
/// recibiría el programa, con las reglas habituales de Windows (<c>CommandLineToArgvW</c> para lo que sigue al nombre del programa):
/// los espacios y tabuladores separan argumentos salvo entre comillas; <c>"</c> abre o cierra un tramo entre comillas; <c>2n</c>
/// barras invertidas seguidas de <c>"</c> son <c>n</c> barras y la comilla funciona como tal; <c>2n+1</c> son <c>n</c> barras y una
/// comilla literal; las barras que no preceden a una comilla son literales. Solo traduce: no ejecuta nada ni expande variables.
/// </summary>
public static class CommandLineArguments
{
    public static IReadOnlyList<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\\')
            {
                var slashes = 0;
                while (index < text.Length && text[index] == '\\')
                {
                    slashes++;
                    index++;
                }

                if (index < text.Length && text[index] == '"')
                {
                    current.Append('\\', slashes / 2);
                    hasToken = true;
                    if (slashes % 2 == 1)
                    {
                        current.Append('"');
                        index++;
                    }
                }
                else
                {
                    current.Append('\\', slashes);
                    hasToken = true;
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                index++;
                continue;
            }

            if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                index++;
                continue;
            }

            current.Append(c);
            hasToken = true;
            index++;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
