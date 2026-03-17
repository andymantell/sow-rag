"""
Converts a PDF to markdown using pymupdf4llm and writes the result to stdout (UTF-8).
Usage: python pdf_to_markdown.py <pdf_path>
Install: pip install pymupdf4llm
"""
import sys

try:
    import pymupdf.layout  # Activate Layout Mode for header/footer detection
    import pymupdf4llm
except ImportError:
    print("pymupdf4llm is not installed. Run: pip install pymupdf4llm", file=sys.stderr)
    sys.exit(1)

if len(sys.argv) < 2:
    print("Usage: pdf_to_markdown.py <pdf_path>", file=sys.stderr)
    sys.exit(1)

try:
    md = pymupdf4llm.to_markdown(sys.argv[1], header=False, footer=False, use_ocr=False)
    sys.stdout.buffer.write(md.encode("utf-8"))
except Exception as e:
    print(f"Error converting PDF: {e}", file=sys.stderr)
    sys.exit(1)
