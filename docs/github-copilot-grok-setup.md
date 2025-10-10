# Enabling Grok Code Fast 1 in GitHub Copilot

This guide explains how to enable and use Grok Code Fast 1 (by xAI) as your AI model in GitHub Copilot.

## Prerequisites

- **GitHub Copilot subscription** (Individual, Business, or Enterprise)
- **Visual Studio Code** (version 1.85 or later) with the GitHub Copilot extension installed
- **Access to model selection** - This feature requires GitHub Copilot with access to multiple models

## What is Grok Code Fast 1?

Grok Code Fast 1 is xAI's fast language model optimized for coding tasks. It provides:
- Fast response times for code completions
- Strong understanding of multiple programming languages
- Efficient code generation and suggestions
- Lower latency compared to larger models

## Enabling Grok Code Fast 1

### Method 1: Using VS Code Settings (Recommended)

1. **Open VS Code Settings**
   - Press `Ctrl + ,` (Windows/Linux) or `Cmd + ,` (Mac)
   - Or go to **File** > **Preferences** > **Settings**

2. **Search for Copilot Model**
   - Type "copilot model" in the search bar
   - Look for "GitHub Copilot: Model"

3. **Select Grok Code Fast 1**
   - In the dropdown menu, select `grok-code-fast-1` or `xai/grok-code-fast-1`
   - The setting will be saved automatically

### Method 2: Using settings.json

1. **Open VS Code settings.json**
   - Press `Ctrl + Shift + P` (Windows/Linux) or `Cmd + Shift + P` (Mac)
   - Type "Preferences: Open User Settings (JSON)"
   - Press Enter

2. **Add the Copilot model configuration**
   ```json
   {
     "github.copilot.model": "grok-code-fast-1"
   }
   ```

3. **Save the file**
   - Press `Ctrl + S` (Windows/Linux) or `Cmd + S` (Mac)

### Method 3: Using Copilot Chat Settings

1. **Open GitHub Copilot Chat**
   - Click the Copilot icon in the Activity Bar (left sidebar)
   - Or press `Ctrl + Shift + I` (Windows/Linux) or `Cmd + Shift + I` (Mac)

2. **Access Model Selector**
   - Look for a model selector dropdown at the top of the chat panel
   - Click on the current model name

3. **Select Grok Code Fast 1**
   - Choose "Grok Code Fast 1" or "xai/grok-code-fast-1" from the list

## Verifying the Configuration

To verify that Grok Code Fast 1 is enabled:

1. **Check the active model**
   - Open Copilot Chat
   - Look at the model indicator at the top of the chat window
   - It should display "Grok Code Fast 1" or similar

2. **Test code completion**
   - Open any code file
   - Start typing a function or code comment
   - Observe the Copilot suggestions
   - The response time should be notably fast

3. **Check settings**
   - Open VS Code settings
   - Search for "github.copilot.model"
   - Verify it shows "grok-code-fast-1"

## Project-Specific Configuration

To set Grok Code Fast 1 for this specific project:

1. **Create or edit `.vscode/settings.json`** in the project root:
   ```json
   {
     "github.copilot.model": "grok-code-fast-1"
   }
   ```

2. **Commit the settings** (optional)
   - If you want all team members to use the same model
   - Add `.vscode/settings.json` to version control
   - Note: Individual users can override this in their user settings

## Alternative Models Available

If you want to switch models, here are other common options:

- `gpt-4` - OpenAI GPT-4 (slower but more capable)
- `gpt-3.5-turbo` - OpenAI GPT-3.5 (balanced speed and capability)
- `claude-3.5-sonnet` - Anthropic Claude 3.5 Sonnet
- `o1-preview` - OpenAI O1 Preview (reasoning model)
- `o1-mini` - OpenAI O1 Mini (fast reasoning)

## Troubleshooting

### Model Not Available

If "grok-code-fast-1" doesn't appear in the dropdown:

1. **Update GitHub Copilot extension**
   - Open Extensions view (`Ctrl + Shift + X`)
   - Search for "GitHub Copilot"
   - Click "Update" if available

2. **Check your subscription**
   - Verify you have an active Copilot subscription
   - Some models require specific subscription tiers
   - Visit https://github.com/settings/copilot

3. **Restart VS Code**
   - Close and reopen VS Code
   - The model list may refresh

### Slow Performance

If Grok Code Fast 1 seems slow:

1. **Check your internet connection**
   - Ensure you have a stable connection
   - Test with other models to compare

2. **Clear Copilot cache**
   - Open Command Palette (`Ctrl + Shift + P`)
   - Type "Developer: Reload Window"
   - Press Enter

3. **Check VS Code version**
   - Ensure you're using VS Code 1.85 or later
   - Update if necessary

### Settings Not Taking Effect

If the model selection doesn't change:

1. **Check settings precedence**
   - User settings override workspace settings
   - Verify which settings.json is being used

2. **Reload window**
   - Press `Ctrl + Shift + P`
   - Type "Developer: Reload Window"
   - Press Enter

3. **Check for conflicts**
   - Search for "github.copilot.model" in all settings
   - Ensure there are no conflicting configurations

## Best Practices

1. **Choose based on task**
   - Use Grok Code Fast 1 for quick completions and iterations
   - Switch to GPT-4 for complex architectural decisions

2. **Project consistency**
   - Document the recommended model in your README
   - Consider adding it to `.vscode/settings.json`

3. **Monitor performance**
   - Different models excel at different tasks
   - Experiment to find what works best for your workflow

## Additional Resources

- [GitHub Copilot Documentation](https://docs.github.com/en/copilot)
- [VS Code GitHub Copilot Extension](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot)
- [xAI Grok Documentation](https://x.ai/grok)
- [GitHub Copilot Settings Reference](https://code.visualstudio.com/docs/copilot/copilot-settings)

## Notes

- Model availability may vary based on your subscription tier
- Performance characteristics may change as models are updated
- Some features may require specific Copilot extension versions
- The exact model name format may vary (`grok-code-fast-1` vs `xai/grok-code-fast-1`)
