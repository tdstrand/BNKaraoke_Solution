# Quick Start Guide

## GitHub Copilot with Grok Code Fast 1

This project is pre-configured to use **Grok Code Fast 1** for AI-assisted development.

### First Time Setup (5 minutes)

1. **Install VS Code** (if not already installed)
   - Download from https://code.visualstudio.com/

2. **Install GitHub Copilot Extension**
   - Open VS Code
   - Press `Ctrl + Shift + X` (or `Cmd + Shift + X` on Mac)
   - Search for "GitHub Copilot"
   - Click "Install"

3. **Sign in to GitHub**
   - You'll be prompted to sign in
   - Follow the authentication steps

4. **Open this project**
   - The `.vscode/settings.json` file already configures Grok Code Fast 1
   - No additional configuration needed!

### Verify It's Working

1. Open any code file (e.g., `BNKaraoke.Api/Program.cs`)
2. Start typing a comment or function
3. You should see Copilot suggestions appear (usually in gray text)
4. Press `Tab` to accept a suggestion

### Need More Details?

See the comprehensive guide: [GitHub Copilot Grok Setup](github-copilot-grok-setup.md)

### Switch Models (Optional)

To use a different AI model:
1. Open Settings (`Ctrl + ,`)
2. Search for "github.copilot.model"
3. Select your preferred model from the dropdown

Available models include:
- `grok-code-fast-1` (default - fastest)
- `gpt-4` (more capable, slower)
- `claude-3.5-sonnet`
- `o1-preview`
