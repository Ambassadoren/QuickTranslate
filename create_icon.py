from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent
image = Image.new('RGBA', (512, 512))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((12, 12, 500, 500), radius=115, fill='#7156D9')
draw.rounded_rectangle((67, 94, 308, 310), radius=42, fill='white')
draw.polygon([(95, 293), (95, 355), (160, 299)], fill='white')
draw.rounded_rectangle((225, 225, 449, 419), radius=39, fill='#DAD1FF', outline='#7156D9', width=12)
draw.polygon([(360, 407), (420, 457), (420, 400)], fill='#DAD1FF')
font_en = ImageFont.truetype(r'C:\Windows\Fonts\segoeuib.ttf', 146)
font_ru = ImageFont.truetype(r'C:\Windows\Fonts\segoeuib.ttf', 132)
draw.text((129, 108), 'A', font=font_en, fill='#7156D9')
draw.text((286, 232), 'Я', font=font_ru, fill='#5336B1')
image.save(root/'QuickTranslate/app.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
image.resize((64,64), Image.Resampling.LANCZOS).save(root/'QuickTranslate/app.png')
image.save(root/'qa-icon.png')
print('Icon created')
