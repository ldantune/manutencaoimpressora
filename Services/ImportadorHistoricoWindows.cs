using Windows.Networking.Connectivity;

namespace ManutencaoImpressora.Services;

// ---------------------------------------------------------------
//  Importa o uso de internet por app que o próprio Windows registra
//  (a mesma informação de Configurações › Rede › Uso de dados), para
//  o L2 já mostrar os dias de antes de o monitor ser instalado.
//  O Windows costuma guardar de 30 a 60 dias.
//  Os nomes técnicos do Windows são traduzidos pelo TradutorDeApps.
// ---------------------------------------------------------------
public static class ImportadorHistoricoWindows
{
    const int DiasParaTras = 60;
    // v2: nomes traduzidos (a v1 gravava os nomes técnicos; trocar a chave reimporta tudo)
    const string ChaveImportadoAte = "historico_windows_importado_ate_v2";

    // Importa os dias que ainda faltam; devolve quantos dias foram consultados
    public static async Task<int> ImportarAsync(FonteUsoRede fonte)
    {
        var hoje = DateTime.Today;
        // Depois que o monitor começa, ele é a fonte oficial: só importa os dias anteriores
        var limite = (fonte.Monitor.PrimeiroDiaApps() ?? hoje.AddDays(1)).AddDays(-1);
        if (limite > hoje) limite = hoje;

        var inicio = hoje.AddDays(-DiasParaTras);
        // Dias já importados não mudam mais (exceto o último, que pode ter ficado incompleto)
        if (DateTime.TryParse(fonte.Local.LerMeta(ChaveImportadoAte), out var importadoAte) && importadoAte > inicio)
            inicio = importadoAte;
        if (inicio > limite) return 0;

        var tradutor = new TradutorDeApps();
        var estados = new NetworkUsageStates { Roaming = TriStates.DoNotCare, Shared = TriStates.DoNotCare };
        var perfis = NetworkInformation.GetConnectionProfiles();
        var porDiaEApp = new Dictionary<(DateTime Dia, string App), (string Caminho, long Recebidos, long Enviados)>();
        int diasConsultados = 0;

        for (var dia = inicio; dia <= limite; dia = dia.AddDays(1))
        {
            diasConsultados++;
            foreach (var perfil in perfis)
            {
                IReadOnlyList<AttributedNetworkUsage> usos;
                try
                {
                    usos = await perfil.GetAttributedNetworkUsageAsync(
                        new DateTimeOffset(dia), new DateTimeOffset(dia.AddDays(1)), estados);
                }
                catch { continue; }   // perfil sem dados ou sem permissão

                foreach (var uso in usos)
                {
                    if (uso.BytesReceived == 0 && uso.BytesSent == 0) continue;
                    var (nome, caminho) = tradutor.Traduzir(uso.AttributionId, uso.AttributionName);
                    if (string.IsNullOrWhiteSpace(nome)) continue;

                    var chave = (dia, nome);
                    porDiaEApp.TryGetValue(chave, out var atual);
                    porDiaEApp[chave] = (caminho.Length > 0 ? caminho : atual.Caminho ?? "",
                        atual.Recebidos + (long)uso.BytesReceived, atual.Enviados + (long)uso.BytesSent);
                }
            }
        }

        fonte.Local.SubstituirAppsWindows(inicio, limite,
            porDiaEApp.Select(kv => (kv.Key.Dia, kv.Key.App, kv.Value.Caminho, kv.Value.Recebidos, kv.Value.Enviados)));
        fonte.Local.GravarMeta(ChaveImportadoAte, limite.ToString("yyyy-MM-dd"));
        return diasConsultados;
    }
}
