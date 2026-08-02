# PHD Eng. Digital — Navisworks Plugin

Plugin para **Autodesk Navisworks Manage / Simulate 2026** desenvolvido pela PHD Eng. Digital.  
Adiciona uma aba **"PHD Eng. Digital"** ao ribbon do Navisworks com seis painéis de ferramentas para exportação, verificação e quantificação de modelos BIM.

---

## Instalação rápida

1. Baixe `PHD_NavisPlugin_1.0.0_Setup.exe` na [página de releases](../../releases/latest)
2. Feche o Navisworks
3. Execute o instalador — **não requer permissões de administrador**
4. Abra o Navisworks → a aba **PHD Eng. Digital** aparece automaticamente

O instalador copia os arquivos para:
```
%AppData%\Autodesk\Navisworks 2026\Plugins\PHDNavisTools\
```

---

## Requisitos

| Componente | Versão |
|---|---|
| Autodesk Navisworks Manage ou Simulate | **2026** |
| Windows | 10 / 11 |
| .NET Framework | 4.8 (incluído no Windows) |

---

## Visão geral dos painéis

| Painel | Ferramentas | Status |
|---|---|---|
| **IFC Export** | Exportar IFC, Exportar por Search Set, Exportar Seleção IFC | ✅ Validado |
| **Clash Detection** | Export Clash Results (CSV) | ✅ Validado |
| **FBX** | Exportar FBX por Sets | ⚠️ Aguarda testes |
| **QTO** | QTO Auto Attach | ⚠️ Aguarda testes |
| **Check** | Verificar Propriedades (CSV), Verificar IDS | ✅ / ⚠️ |
| **View** | Realçar Seleção, Restaurar Aparência | ⚠️ Aguarda testes |

**Legenda:** ✅ Testado e validado em produção · ⚠️ Implementado, aguarda validação em campo · 🚧 Em desenvolvimento

---

## Painel IFC Export ✅

Exporta o modelo aberto no Navisworks para o formato **IFC 4**.

### Exportar IFC
Exporta o modelo completo.

**Validado:** exportação com geometria tessellada, mapeamento de categorias Navisworks → tipos IFC, extração de Psets e propriedades customizadas.

**Configurações disponíveis:**
- Pasta de destino
- Autor e organização (cabeçalho IFC)
- Inclusão ou não de elementos ocultos

### Exportar por Search Set
Exporta cada Search Set do modelo como um arquivo IFC separado, útil para separar disciplinas ou lotes.

### Exportar Seleção IFC
Exporta apenas os elementos selecionados no momento da execução.

> **Nota técnica:** A extração de geometria usa a API COM do Navisworks (`ToInwOpSelection` + `PrimitiveSink` com matriz de transformação), baseada na abordagem do projeto [BIMCamel](https://github.com/mrshoma99-rgb/bimcamel-ifc-exporter). O identificador IFC (`GlobalId`) usa o algoritmo BigInteger determinístico do BIMCamel, garantindo estabilidade entre re-exportações.

---

## Painel Clash Detection ✅

### Export Clash Results
Exporta todos os resultados de clash detection do documento para um arquivo **CSV**, incluindo nome do clash, status, elementos envolvidos e posição 3D.

**Validado:** funciona com modelos multi-disciplina e múltiplos grupos de clash.

---

## Painel FBX ⚠️

### Exportar FBX por Sets
Exporta o modelo segmentado por Search Sets para o formato FBX, útil para visualização em engines 3D e plataformas de realidade virtual.

> **Status:** implementado, ainda não validado em campo.

---

## Painel QTO ⚠️

### QTO Auto Attach
Vincula automaticamente propriedades de quantitativos (QTO) a elementos do modelo usando mapeamento por Search Sets ou por propriedades diretas, de acordo com regras definidas em CSV/Excel.

**Modos de operação:**
- **Por Search Set:** associa quantitativos a todos os elementos de cada Search Set
- **Por propriedade:** associa com base em correspondência de valor de propriedade

**Progresso:** barra de progresso visual com percentual em tempo real.

> **Status:** implementado com controle de progresso assíncrono. Ainda não validado em campo com planilha real de QTO.

---

## Painel Check

### Verificar Propriedades (CSV/Excel) ✅

Verifica se propriedades específicas estão preenchidas em elementos do modelo, de acordo com regras definidas em um arquivo CSV ou Excel.

**Validado em campo com modelo NWC (estrutura de concreto pré-moldado):**
- Filtragem por disciplina (substring do nome do arquivo-fonte)
- Travessia de 136 000+ elementos
- Resultados com identificador por `DisplayName` (compatível com modelos não-Revit)
- Log de performance via `PluginLogger`

#### Formato do CSV de regras

| Disciplina | Categoria | Propriedade | CategoriaFiltro | PropriedadeFiltro |
|---|---|---|---|---|
| EST | Pset_BeamCommon | IsExternal | | |
| ARQ | Pset_WallCommon | FireRating | IFC | Entity |
| EST | Pset_ColumnCommon | LoadBearing | | |

- **Disciplina:** substring do nome do arquivo-fonte (ex: `"EST"` filtra arquivos com `"EST"` no nome). Deixe vazio para aplicar a todos os modelos.
- **Categoria:** nome exato da aba/categoria de propriedade no Navisworks (ex: `"Pset_BeamCommon"`)
- **Propriedade:** nome exato da propriedade dentro da categoria
- **CategoriaFiltro + PropriedadeFiltro** *(opcionais):* a regra só é aplicada a elementos que possuam a propriedade `PropriedadeFiltro` na categoria `CategoriaFiltro` com valor não-vazio. Útil para filtrar por tipo IFC, função estrutural, etc.

Aceita separadores `;` ou `,` e cabeçalhos em português ou inglês (case-insensitive).

#### Resultados

| Resultado | Significado |
|---|---|
| ✓ Preenchida | Propriedade existe e tem valor |
| ⚠ Vazia | Propriedade existe mas está vazia |
| ✗ Ausente | Categoria ou propriedade não encontrada |

#### Aba Resumo ⚠️
Exibe o **percentual de preenchimento** por Disciplina e por Source File com barras visuais (`█░`).

> **Status:** implementado. A lógica de resumo foi revisada, mas a aba ainda não foi validada em campo com dados reais.

#### Performance
- Cache de `PropertyCategories` por item (1 scan COM → todas as regras usam dicionário em memória)
- Progresso assíncrono via `Dispatcher.Yield(DispatcherPriority.Background)` — UI não trava
- Log detalhado em `logs/plugin.log` com tempo por fase e estatísticas por regra

---

### Verificar IDS ⚠️

Valida o modelo contra um arquivo **IDS (Information Delivery Specification)** no padrão [buildingSMART](https://www.buildingsmart.org/standards/bsi-standards/information-delivery-specification/).

#### O que é IDS
IDS é um formato XML aberto que define quais propriedades, entidades IFC e classificações são obrigatórias em um modelo BIM. Permite verificação automática de conformidade com requisitos de entrega.

#### Funcionalidades implementadas
- Parser XML com matching por `LocalName` (robusto a prefixos de namespace `ids:` ou sem prefixo)
- Facets suportados: **entity**, **property**, **attribute**, **classification**
- Restrições de valor: `simpleValue`, `enumeration`, `pattern` (regex)
- Cardinalidade: `required`, `optional`, `prohibited`
- Resultado por elemento: **✓ PASS**, **✗ FAIL**, **— N/A**
- Grid colorida (verde/vermelho/cinza), exportação CSV
- Limite de 10 000 resultados para evitar sobrecarga da interface
- Log de performance com marks a cada 500 itens

#### Performance
Cada item do modelo é lido **uma única vez** via COM (cache `Dictionary<categoria, Dictionary<propriedade, valor>>`). Todas as avaliações de facets usam este cache, evitando re-scan por spec. Ganho estimado: ~14× em relação à abordagem ingênua.

#### Identificação do tipo de entidade IFC
O motor busca o tipo IFC do elemento procurando uma propriedade com um destes nomes em qualquer categoria:
`Entity`, `IFC Type`, `IfcType`, `IFC Entity`, `EntityType`

> **Status:** implementado e com otimização de performance aplicada. **Não validado em campo** — o mapeamento do tipo de entidade IFC pode variar conforme a origem do modelo (NWC, IFC nativo, RVT). Um log de diagnóstico imprime as categorias/propriedades do primeiro elemento ao iniciar a verificação — use o arquivo `logs/plugin.log` para calibrar se necessário.

> **Conhecido:** com `onlyFailures=False` e muitas specs, o volume de resultados cresce rapidamente. Recomenda-se usar **"Exibir apenas falhas"** para modelos grandes.

---

## Painel View ⚠️

### Realçar Seleção
Aplica sobreposições de cor por categoria aos elementos selecionados, com preview em tempo real e barra de progresso.

**Fases de execução:**
1. Expansão da seleção (0–40%)
2. Varredura de geometria (40–90%)
3. Aplicação das sobreposições (90–100%)

> **Status:** implementado com progresso assíncrono. O fluxo de override de materiais foi revisado, mas ainda não validado com seleção grande em campo.

### Restaurar Aparência
Remove todas as sobreposições de cor e transparência temporárias do modelo com um clique.

> **Status:** implementado. Operação simples (`ResetAllTemporaryMaterials`), baixo risco.

---

## Logging e diagnóstico

Todos os comandos gravam logs em:
```
c:\dev\navisworks-ifc-exporter\logs\plugin.log   (ambiente de desenvolvimento)
```

Cada execução inclui:
- Timestamps por fase
- Contagem de elementos processados
- Tempo total e throughput (itens/segundo) via `PerfScope`
- Estatísticas por regra (Check CSV)
- Diagnóstico de propriedades do primeiro elemento (IDS)

---

## Compilar do fonte

### Pré-requisitos
- .NET SDK 6+ com suporte a `net48`
- Navisworks Manage 2026 instalado (para as referências de API)

### Compilar
```powershell
git clone https://github.com/JoseARVargas/navisworks-ifc-exporter.git
cd navisworks-ifc-exporter
dotnet build -c Release
```

O build copia automaticamente o DLL para:
```
%AppData%\Autodesk\Navisworks 2026\Plugins\PHDNavisTools\
```

### Rodar os testes
```powershell
dotnet test Tests\
```

Não requer Navisworks em execução — os testes cobrem apenas lógica pura (sem instanciar tipos da API). Esperado: **88 testes, 0 falhas**.

### Gerar o instalador
Requer [Inno Setup 6+](https://jrsoftware.org/isdl.php) instalado.

```powershell
cd installer
.\build_installer.ps1
# Gera: installer/output/PHD_NavisPlugin_1.0.0_Setup.exe
```

---

## Testes automatizados

O projeto `Tests/PHDNavisTools.Tests.csproj` (xUnit 2.6, .NET 4.8) cobre a camada de lógica pura do plugin — sem necessidade de Navisworks em execução.

### O que é testado

| Arquivo de teste | O que cobre | Testes |
|---|---|---|
| `IdsValueTests.cs` | `IdsValue.Matches` (simpleValue, enumeração, regex, null) | 22 |
| `IdsParserTests.cs` | `IdsParser.ParseFile` — parsing com/sem prefixo `ids:`, todos os facets, erros de XML | 17 |
| `IdsServiceEvalTests.cs` | Motor de validação IDS: `EvalFacet`, `MatchesApplicability`, `CheckRequirements`, `GetEntityTypeFromCache` | 27 |
| `CheckServiceTests.cs` | `CheckPropertyFromCache`, `ItemMatchesFilterFromCache`, `LoadRules` (CSV `;` e `,`, colunas de filtro), `CheckSummaryRow` | 22 |

### O que não é testado automaticamente

A API do Navisworks exige um processo host rodando — os seguintes pontos precisam de validação manual em campo:

- `GetGeometryItems` / `BuildPropCache` — leitura COM de `ModelItem`
- Janelas WPF — fluxo completo de UI
- IDS contra modelo real — mapeamento do tipo de entidade IFC varia por origem do arquivo (NWC, IFC nativo)

### Fixtures de teste

Os arquivos em `Tests/TestData/` cobrem os cenários principais:

| Arquivo | Propósito |
|---|---|
| `sample_minimal.ids` | IDS mínimo sem namespace |
| `sample_ns_prefix.ids` | Mesmo conteúdo com prefixo `ids:` — valida parsing namespace-agnóstico |
| `sample_full.ids` | Todos os facets: entity, property (enumeração), attribute, classification, prohibited |
| `rules_basic.csv` | CSV básico com separador `;` |
| `rules_comma.csv` | CSV com separador `,` |
| `rules_with_filter.csv` | CSV com colunas `CategoriaFiltro` e `PropriedadeFiltro` |

---

## Estrutura do projeto

```
PHDNavisTools.sln
│
├── PHDNavisTools.csproj     # Plugin principal
│   ├── Plugin.cs                    # Registro de todos os AddInPlugins
│   ├── RibbonLoader.cs              # Construção da aba PHD no ribbon
│   │
│   ├── Core/
│   │   ├── ExportService.cs         # Orquestra exportação IFC
│   │   ├── GeometryExtractor.cs     # Extrai geometria via COM (ToInwOpSelection)
│   │   ├── PropertyExtractor.cs     # Lê propriedades dos elementos
│   │   ├── IfcWriter.cs             # Gera arquivo IFC via xBIM
│   │   ├── IfcTypeMapper.cs         # Mapeia categorias → tipos IFC
│   │   ├── CheckService.cs          # Motor de verificação de propriedades (CSV)
│   │   ├── IdsModels.cs             # Modelos de dados IDS
│   │   ├── IdsParser.cs             # Parser XML do arquivo .ids
│   │   ├── IdsService.cs            # Motor de validação IDS
│   │   ├── QtoService.cs            # Lógica de QTO Auto Attach
│   │   └── PluginLogger.cs          # Logger + PerfScope para métricas
│   │
│   ├── Models/
│   │   └── ElementData.cs           # DTO de elemento para exportação IFC
│   │
│   ├── UI/
│   │   ├── ExportWindow.xaml(.cs)
│   │   ├── CheckWindow.xaml(.cs)
│   │   ├── IdsWindow.xaml(.cs)
│   │   ├── QtoWindow.xaml(.cs)
│   │   ├── HighlightSelectionWindow.xaml(.cs)
│   │   └── ClashResultsWindow.xaml(.cs)
│   │
│   ├── Resources/                   # Ícones PNG 32×32
│   └── installer/
│       ├── PHD_NavisPlugin.iss      # Script Inno Setup
│       ├── build_installer.ps1
│       └── generate_assets.ps1
│
└── Tests/PHDNavisTools.Tests.csproj   # Testes unitários
    ├── IdsValueTests.cs
    ├── IdsParserTests.cs
    ├── IdsServiceEvalTests.cs
    ├── CheckServiceTests.cs
    └── TestData/                    # Fixtures .ids e .csv
```

---

## Dependências

| Biblioteca | Versão | Uso | Licença |
|---|---|---|---|
| [xBIM Essentials](https://github.com/xBimTeam/XbimEssentials) | 5.1.341 | Escrita de arquivo IFC 4 | CDDL 1.0 |
| [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) | 3.7.0 | Leitura de planilhas Excel | MIT |

As DLLs do Navisworks (`Autodesk.Navisworks.Api`, etc.) são referenciadas localmente e **não redistribuídas**.

---

## Licença

MIT — veja [LICENSE](LICENSE).
