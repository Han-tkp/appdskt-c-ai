import re

file_path = r'c:\Users\h4n\Desktop\app-new12-2\DropDetect\MainWindow.axaml'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

replacements = {
    '#1E1E2E': '{DynamicResource ThemeBackgroundBrush}',
    '#282A36': '{DynamicResource ThemePanelBrush}',
    '#181825': '{DynamicResource ThemeSubPanelBrush}',
    '#313244': '{DynamicResource ThemeBorderBrush}',
    '#CDD6F4': '{DynamicResource ThemeTextPrimaryBrush}',
    '#A6ADC8': '{DynamicResource ThemeTextSecondaryBrush}',
    '#89B4FA': '{DynamicResource ThemeAccentBlueBrush}',
    '#A6E3A1': '{DynamicResource ThemeAccentGreenBrush}',
    '#F38BA8': '{DynamicResource ThemeAccentRedBrush}',
    '#F9E2AF': '{DynamicResource ThemeAccentYellowBrush}',
    '#11111B': '{DynamicResource ThemeActionTextBrush}',
    '#45475A': '{DynamicResource ThemeBorderBrush}'
}

for hex_code, resource in replacements.items():
    content = content.replace(hex_code, resource)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)

print('Colors replaced successfully.')
