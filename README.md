# PHDNavisTools — Plugin para Autodesk Navisworks

Plugin desenvolvido pela **PHD Engenharia Digital** para Autodesk Navisworks Manage 2025/2026/2027.  
Adiciona uma aba **"PHD Eng. Digital"** ao ribbon com 13 ferramentas cobrindo exportação IFC/FBX/CSV, verificação de propriedades e IDS, gerenciamento e cascateamento de propriedades, importação de dados Excel, criação de Search Sets por propriedade, clash detection e quantificação de modelos BIM.

---

## Instalação

1. Baixe `PHDNavisTools-vX.Y.Z.zip` na [página de releases](../../releases/latest)
2. Extraia o ZIP em qualquer pasta
3. Feche o Navisworks
4. Execute **`Instalar.bat`** — detecta automaticamente as versões 2025, 2026 e 2027 instaladas
5. Abra o Navisworks → a aba **PHD Eng. Digital** aparece no ribbon

Para remover o plugin, execute **`Desinstalar.bat`** (incluído no ZIP).

### Requisitos

| Componente | Versão |
|---|---|
| Autodesk Navisworks Manage | 2025, 2026 ou 2027 |
| Windows | 10 / 11 |
| .NET Framework | 4.8 (já incluso no Windows) |

---

## Painéis e comandos

### PHD
| Botão | Descrição |
|---|---|
| **Site** | Abre o site da PHD Engenharia Digital |

---

### IFC Export
| Botão | Descrição |
|---|---|
| **Exportar IFC** | Exporta o modelo completo para IFC 4 |
| **Exportar por Search Set** | Exporta cada Search Set como um arquivo IFC separado |
| **Exportar Seleção IFC** | Exporta apenas os elementos selecionados |

---

### Clash Detection
| Botão | Descrição |
|---|---|
| **Export Clash Results** | Exporta todos os resultados de clash detection para CSV |

---

### FBX
| Botão | Descrição |
|---|---|
| **Exportar FBX por Sets** | Exporta o modelo segmentado por Search Sets para FBX |

---

### QTO
| Botão | Descrição |
|---|---|
| **QTO Auto Attach** | Vincula propriedades de quantitativos a elementos via CSV/Excel |

---

### Check
| Botão | Descrição |
|---|---|
| **Verificar Propriedades** | Verifica preenchimento de propriedades segundo regras CSV/Excel |
| **Verificar IDS** | Valida o modelo contra um arquivo IDS (buildingSMART) |
| **Importar Excel** | Importa propriedades de uma planilha Excel para elementos do modelo |
| **Set → Propriedade** | Copia o nome do Search/Selection Set como propriedade dos elementos do set |
| **Limpar Propriedades** | Remove abas ou propriedades específicas de elementos do modelo |
| **Cascatear Propriedade** | Propaga o valor de uma propriedade de elementos pai para todos os seus descendentes |

---

### View
| Botão | Descrição |
|---|---|
| **Realçar Seleção** | Aplica sobreposições de cor por categoria nos elementos selecionados |
| **Restaurar Aparência** | Remove todas as sobreposições de cor e transparência do modelo |

---

## Detalhes dos comandos de propriedades

### Set → Propriedade
Selecione um ou mais Search Sets ou Selection Sets e execute o comando. O nome do set é gravado como propriedade customizada em todos os elementos que pertencem a ele.

- Configura a **aba** e o **nome da propriedade** de destino
- Opção de **sobrescrever** ou **manter** valores já existentes nos elementos

### Limpar Propriedades
Remove propriedades customizadas dos elementos do modelo.

- Selecione a **aba** e as **propriedades** a remover (checkboxes)
- Opera em todos os elementos do modelo ou apenas nos selecionados
- Remover todas as propriedades de uma aba remove a aba inteira

### Cascatear Propriedade
Selecione os elementos **pai** no Navisworks e execute. O valor da propriedade é lido de cada pai e gravado em todos os seus descendentes na hierarquia.

- Escolha a **propriedade** (campo obrigatório)
- Marque as **abas** onde pesquisar — nenhuma marcada = pesquisa em todas; marcar abas específicas melhora a performance
- Se a propriedade estiver em abas diferentes entre os pais, todas são cascateadas independentemente
- Opção de **criar** a propriedade nos descendentes que não a têm, ou **atualizar apenas quem já tem**
- Barra de progresso em tempo real: atualiza durante a coleta de descendentes (a cada 5.000 itens) e durante a gravação (a cada 10.000 itens)

---

## Compilar do fonte

### Pré-requisitos
- .NET SDK 6+ com suporte a `net48`
- Autodesk Navisworks Manage 2025, 2026 ou 2027 instalado

### Build de desenvolvimento (Debug)
```powershell
git clone https://github.com/JoseARVargas/PHDNavisTools.git
cd PHDNavisTools
dotnet build -c Debug
```

O build Debug copia automaticamente o DLL para a pasta de plugins do Navisworks instalado.

### Publicar uma nova release

```powershell
# 1. Faça commit e tag da versão
git tag v1.7.0
git push origin v1.7.0

# 2. Execute o script de release
.\scripts\Make-Release.ps1 -Version "1.7.0" -Title "v1.7.0 - Descrição" -NotesFile "notes.md"
```

O script `Make-Release.ps1`:
- Compila em modo **Release**
- Empacota `PHDNavisTools.dll` + dependências + `Instalar.bat` + `Desinstalar.bat` em um ZIP
- Publica a release no GitHub com o ZIP como asset para download

---

## Estrutura do projeto

```
PHDNavisTools/
├── Plugin.cs                    # Registro de todos os comandos (AddInPlugin)
├── RibbonLoader.cs              # Construção da aba e painéis no ribbon
│
├── Core/
│   ├── PropertyWriter.cs        # Gravação de propriedades via COM API
│   ├── PropertyEraser.cs        # Remoção de propriedades via COM API
│   ├── NavisPropertyScanner.cs  # Varredura de abas/propriedades do modelo
│   ├── SetPropertyService.cs    # Lógica: Set → Propriedade
│   ├── CascadePropertyService.cs # Lógica: Cascatear Propriedade
│   ├── CheckService.cs          # Lógica: Verificar Propriedades (CSV/Excel)
│   ├── IdsParser.cs             # Parser XML do arquivo .ids
│   ├── IdsService.cs            # Motor de validação IDS
│   ├── QtoService.cs            # Lógica: QTO Auto Attach
│   └── PluginLogger.cs          # Logger e métricas de performance
│
├── UI/
│   ├── SetsToPropertyWindow.xaml(.cs)
│   ├── ClearPropertiesWindow.xaml(.cs)
│   ├── CascadePropertyWindow.xaml(.cs)
│   ├── ExportWindow.xaml(.cs)
│   ├── CheckWindow.xaml(.cs)
│   ├── IdsWindow.xaml(.cs)
│   ├── QtoWindow.xaml(.cs)
│   ├── HighlightSelectionWindow.xaml(.cs)
│   └── ClashResultsWindow.xaml(.cs)
│
├── Resources/                   # Ícones PNG 32×32
├── installer/
│   ├── Instalar.bat             # Instalador (detecta Navisworks 2025/2026/2027)
│   └── Desinstalar.bat
└── scripts/
    └── Make-Release.ps1         # Automatiza build + empacotamento + release GitHub
```

---

## Dependências

| Biblioteca | Versão | Uso | Licença |
|---|---|---|---|
| [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) | 3.7.0 | Leitura de planilhas Excel | MIT |

As DLLs do Navisworks (`Autodesk.Navisworks.Api`, etc.) são referenciadas localmente e **não redistribuídas**.

---

## Licença

MIT — veja [LICENSE](LICENSE).
