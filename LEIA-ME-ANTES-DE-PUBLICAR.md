# KEYENCE CAMERA DTM — pacote completo para publicar no GitHub

Este diretório contém os 78 ficheiros originais descompactados, sem alterações ao código.

O repositório público de destino é https://github.com/renafcampos27/KEYENCE_CAMERA_DTM.

1. Verifica que podes publicar as DLLs KEYENCE, executáveis, .pdb e ficheiros .vs num repositório **público**. A publicação é irreversível do ponto de vista de cópias/espelhos.
2. No Windows, instala o Git e autentica-te no GitHub.
3. Abre PowerShell nesta pasta e executa: `./UPLOAD_TO_GITHUB.ps1`
4. O script clona o repositório, copia **todos** os ficheiros da aplicação, faz commit e push.

Atenção: o ficheiro `FirstStepApp.vbproj` original tem referências a DLLs usando caminhos absolutos/relativos de outra máquina; para compilar noutro PC pode ser necessário atualizar HintPath para o SDK KEYENCE.

Não foram feitos testes em hardware da câmara ou compilação no Windows neste ambiente.
