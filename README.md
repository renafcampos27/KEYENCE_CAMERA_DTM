# KEYENCE CAMERA DTM

Aplicação Windows Forms VB.NET (`FirstStepAppV2`) para integração de leitor KEYENCE SR-X300W (DTM) com uma página web via teclado.

## Estrutura

- `FirstStepApp.sln` — solução do Visual Studio
- `FirstStepApp.vbproj` — projeto VB.NET (.NET Framework)
- `Form1.vb` — lógica da aplicação e controlo do trigger
- `Form1.Designer.vb`, `Form1.resx` — interface Windows Forms
- `My Project/` — ficheiros de configuração e recursos
- `App.config` — configuração da aplicação

## Compilação

Abrir `FirstStepApp.sln` no Visual Studio para Windows; selecionar **x86**. O projeto original referencia DLLs do SDK KEYENCE x86 através de caminhos locais, pelo que poderá ser necessário ajustar `HintPath` e instalar/obter o SDK oficial KEYENCE. Os binários `bin/`, `obj/` e `.vs/` não são código-fonte, e as DLLs de terceiros exigem verificação de licença antes da redistribuição pública.

> Estado: código em desenvolvimento; deve ser validado com o leitor real antes de utilização em produção.
