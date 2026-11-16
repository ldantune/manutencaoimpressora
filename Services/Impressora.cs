using System.Diagnostics;
using System.Drawing.Printing;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Imprime o documento escolhido pelo usuário, ou, se nenhum foi
//  escolhido, a página padrão com faixas de todas as cores
//  (para a tinta da impressora jato de tinta não secar/entupir).
// ---------------------------------------------------------------
public class Impressora
{
    static readonly Color[] Cores =
    {
        Color.Cyan, Color.Magenta, Color.Yellow, Color.Black,
        Color.Red, Color.Green, Color.Blue
    };

    static readonly string[] ExtensoesDeImagem = { ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

    readonly string _nome;
    readonly string _caminhoDocumento;

    public Impressora(string nome, string caminhoDocumento = "")
    {
        _nome = nome;
        _caminhoDocumento = caminhoDocumento;
    }

    public void Imprimir()
    {
        if (!string.IsNullOrWhiteSpace(_nome) && !new PrinterSettings { PrinterName = _nome }.IsValid)
            throw new Exception($"A impressora \"{_nome}\" não foi encontrada.");

        if (string.IsNullOrWhiteSpace(_caminhoDocumento))
            ImprimirPaginaPadrao();
        else if (!File.Exists(_caminhoDocumento))
            throw new Exception($"O arquivo \"{_caminhoDocumento}\" não foi encontrado.");
        else if (ExtensoesDeImagem.Contains(Path.GetExtension(_caminhoDocumento), StringComparer.OrdinalIgnoreCase))
            ImprimirImagem();
        else
            ImprimirDocumentoEscolhido();
    }

    // Desenha a imagem direto na impressora, sem depender de nenhum
    // app externo (o app "Fotos" do Windows, por exemplo, sempre abre
    // o assistente dele e ignora o pedido de impressão silenciosa).
    void ImprimirImagem()
    {
        using var imagem = Image.FromFile(_caminhoDocumento);
        using var doc = new PrintDocument();
        doc.DocumentName = Path.GetFileName(_caminhoDocumento);

        if (!string.IsNullOrWhiteSpace(_nome))
            doc.PrinterSettings.PrinterName = _nome;

        doc.DefaultPageSettings.Color = true;
        doc.PrintController = new StandardPrintController(); // sem janela de progresso

        doc.PrintPage += (s, e) =>
        {
            var area = e.MarginBounds;
            float escala = Math.Min((float)area.Width / imagem.Width, (float)area.Height / imagem.Height);
            int largura = (int)(imagem.Width * escala);
            int altura = (int)(imagem.Height * escala);
            int x = area.X + (area.Width - largura) / 2;
            int y = area.Y + (area.Height - altura) / 2;
            e.Graphics!.DrawImage(imagem, x, y, largura, altura);
        };

        doc.Print();
    }

    // Manda o arquivo escolhido direto para a impressora, usando o
    // próprio programa associado a ele (Word, Adobe Reader, etc).
    void ImprimirDocumentoEscolhido()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _caminhoDocumento,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // "printto" manda direto para a impressora escolhida; nem todo programa
        // aceita esse verbo, então cai para "print" (impressora padrão) se precisar.
        if (!string.IsNullOrWhiteSpace(_nome))
        {
            psi.Verb = "printto";
            psi.Arguments = $"\"{_nome}\"";
            if (TentarImprimir(psi)) return;
        }

        psi.Verb = "print";
        psi.Arguments = "";
        if (!TentarImprimir(psi))
            throw new Exception("Não foi possível imprimir esse arquivo automaticamente. " +
                                 "O programa associado a ele pode não aceitar impressão sem abrir uma janela.");
    }

    static bool TentarImprimir(ProcessStartInfo psi)
    {
        try
        {
            using var p = Process.Start(psi);
            p?.WaitForExit(60000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    void ImprimirPaginaPadrao()
    {
        using var doc = new PrintDocument();
        doc.DocumentName = "Manutenção da impressora jato de tinta";

        if (!string.IsNullOrWhiteSpace(_nome))
            doc.PrinterSettings.PrinterName = _nome;

        doc.DefaultPageSettings.Color = true;
        doc.PrintController = new StandardPrintController(); // sem janela de progresso

        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            int x = 50, y = 40, largura = 90, altura = 35; // unidades: 1/100 de polegada

            foreach (var cor in Cores)
            {
                using var pincel = new SolidBrush(cor);
                g.FillRectangle(pincel, x, y, largura, altura);
                x += largura + 10;
            }

            using var fonte = new Font("Arial", 9);
            g.DrawString($"Impressão de manutenção - {DateTime.Now:dd/MM/yyyy HH:mm}",
                fonte, Brushes.Black, 50, y + altura + 10);
        };

        doc.Print();
    }
}
