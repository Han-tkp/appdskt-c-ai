import fitz
import glob
import re
import os

pdf_dir = (
    r"c:\Users\h4n\Desktop\app-new12-2\.agent\skills\my-skills\dropdetect-dev\Documents"
)
pdfs = glob.glob(os.path.join(pdf_dir, "*.pdf"))

keywords = ["µm", "micron", "ไมครอน", "um", "vmd", "ขนาด", "droplet"]

with open("pdf_output.txt", "w", encoding="utf-8") as f:
    f.write(f"Found {len(pdfs)} PDFs in directory.\n")
    for pdf_path in pdfs:
        f.write(f"\n--- Reading {os.path.basename(pdf_path)} ---\n")
        try:
            doc = fitz.open(pdf_path)
            for page_num, page in enumerate(doc):
                text = page.get_text()
                lines = text.split("\n")
                for i, line in enumerate(lines):
                    if any(kw.lower() in line.lower() for kw in keywords):
                        start = max(0, i - 1)
                        end = min(len(lines), i + 2)
                        context = " ".join(lines[start:end])
                        if re.search(r"\d+", context):
                            f.write(f"Page {page_num + 1}: {context.strip()}\n")
        except Exception as e:
            f.write(f"Error reading {pdf_path}: {e}\n")
