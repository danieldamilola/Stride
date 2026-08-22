import os
import re

dir_path = '.'
dark_replacements = {
    '--bg-base': '#0e0d0b',
    '--bg-dark': '#0e0d0b',
    '--bg-surface': '#141310',
    '--bg-card': '#181714',
    '--bg-hover': '#1f1d19'
}
light_replacements = {
    '--bg-base': '#f8f8fa',
    '--bg-dark': '#f8f8fa',
    '--bg-surface': '#ffffff',
    '--bg-card': '#ffffff',
    '--bg-hover': '#f4f4f6'
}

for root, _, files in os.walk(dir_path):
    for filename in files:
        if filename.endswith('.html'):
            filepath = os.path.join(root, filename)
            with open(filepath, 'r', encoding='utf-8') as f:
                content = f.read()
            
            # Since we have @media blocks, let's iterate over :root blocks
            def replacer(match):
                block_header = match.group(1)
                block_content = match.group(2)
                
                # It's light mode if it explicitly says data-theme=light OR it's inside a light prefers-color-scheme.
                # Since my regex only captures the :root part, we won't see @media. 
                # Let's just do a simpler heuristic: if "light" is in the block header, it's light.
                # If "system" is in the block header AND NOT "dark", maybe it's inside the @media query.
                is_light = False
                if 'light' in block_header:
                    is_light = True
                elif 'system' in block_header and 'dark' not in block_header:
                    # this usually means it's the @media (prefers-color-scheme: light) { :root[data-theme=system] } block
                    is_light = True
                    
                replacements = light_replacements if is_light else dark_replacements
                
                for key, val in replacements.items():
                    block_content = re.sub(rf'({key})\s*:\s*#[0-9a-fA-F]+', rf'\1: {val}', block_content)
                    block_content = re.sub(rf'({key})\s*:\s*var\([^)]+\)', rf'\1: {val}', block_content)
                
                return block_header + '{' + block_content + '}'
                
            new_content = re.sub(r'(:root[^\{]*)\{([^\}]+)\}', replacer, content)
            
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(new_content)
            print(f'Updated {filename}')
