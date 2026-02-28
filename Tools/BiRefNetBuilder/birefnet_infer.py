#!/usr/bin/env python3
"""
BiRefNet Background Removal Inference Script
Usage: python birefnet_infer.py <input_path> <output_path> [--model MODEL_NAME] [--size SIZE]

Exit Codes:
  0 - Success
  1 - General error
  2 - No GPU available
  3 - Input file not found
  4 - Model loading failed
  5 - Inference failed
"""

import sys
import os
import argparse
import time

def main():
    parser = argparse.ArgumentParser(description='BiRefNet Background Removal')
    parser.add_argument('input', help='Input image path')
    parser.add_argument('output', help='Output image path')
    parser.add_argument('--model', default='ZhengPeng7/BiRefNet', help='Model name on HuggingFace')
    parser.add_argument('--size', type=int, default=1024, help='Processing size (default: 1024)')
    parser.add_argument('--fp16', action='store_true', help='Use FP16 for faster inference')
    parser.add_argument('--cpu', action='store_true', help='Force CPU inference')
    args = parser.parse_args()

    # Validate input file exists
    if not os.path.isfile(args.input):
        print(f"ERROR: Input file not found: {args.input}", file=sys.stderr)
        sys.exit(3)

    # Check GPU availability
    try:
        import torch
    except ImportError as e:
        print(f"ERROR: Failed to import torch: {e}", file=sys.stderr)
        sys.exit(1)

    if not args.cpu and not torch.cuda.is_available():
        print("ERROR: No GPU available. Use --cpu flag for CPU inference.", file=sys.stderr)
        sys.exit(2)

    device = 'cpu' if args.cpu else 'cuda'
    print(f"[BiRefNet] Using device: {device}")

    # Import remaining dependencies
    try:
        from PIL import Image
        from transformers import AutoModelForImageSegmentation
        from torchvision import transforms
        import torch.nn.functional as F
    except ImportError as e:
        print(f"ERROR: Failed to import dependencies: {e}", file=sys.stderr)
        sys.exit(1)

    # Set cache directory to script location
    script_dir = os.path.dirname(os.path.abspath(__file__))
    cache_dir = os.path.join(script_dir, 'models')
    os.makedirs(cache_dir, exist_ok=True)
    os.environ['HF_HOME'] = cache_dir
    os.environ['TRANSFORMERS_CACHE'] = cache_dir

    # Load model
    print(f"[BiRefNet] Loading model: {args.model}")
    start_time = time.time()
    try:
        model = AutoModelForImageSegmentation.from_pretrained(
            args.model,
            trust_remote_code=True,
            cache_dir=cache_dir
        )
        model.to(device)
        model.eval()
        
        if args.fp16 and device == 'cuda':
            model.half()
            print("[BiRefNet] Using FP16 precision")
    except Exception as e:
        print(f"ERROR: Failed to load model: {e}", file=sys.stderr)
        sys.exit(4)
    
    load_time = time.time() - start_time
    print(f"[BiRefNet] Model loaded in {load_time:.2f}s")

    # Load and preprocess image
    print(f"[BiRefNet] Processing: {args.input}")
    try:
        image = Image.open(args.input).convert('RGB')
        original_size = image.size
        
        # Transform for BiRefNet
        transform = transforms.Compose([
            transforms.Resize((args.size, args.size)),
            transforms.ToTensor(),
            transforms.Normalize(mean=[0.485, 0.456, 0.406], std=[0.229, 0.224, 0.225])
        ])
        
        input_tensor = transform(image).unsqueeze(0).to(device)
        
        if args.fp16 and device == 'cuda':
            input_tensor = input_tensor.half()
            
    except Exception as e:
        print(f"ERROR: Failed to load/preprocess image: {e}", file=sys.stderr)
        sys.exit(5)

    # Run inference
    print("[BiRefNet] Running inference...")
    start_time = time.time()
    try:
        with torch.no_grad():
            outputs = model(input_tensor)
        
        # BiRefNet returns a list of predictions at different scales
        # Use the finest/last prediction
        if isinstance(outputs, (list, tuple)):
            pred = outputs[-1]
        else:
            pred = outputs
        
        # Ensure pred is the right shape (B, 1, H, W) or (B, H, W)
        if pred.dim() == 3:
            pred = pred.unsqueeze(1)
        
        # Sigmoid to get mask probabilities
        pred = torch.sigmoid(pred)
        
        # Resize mask to original image size
        pred = F.interpolate(pred, size=original_size[::-1], mode='bilinear', align_corners=False)
        
        # Convert to numpy
        mask = pred.squeeze().cpu().float().numpy()
        
        # Threshold to get binary mask
        mask = (mask * 255).astype('uint8')
        
    except Exception as e:
        print(f"ERROR: Inference failed: {e}", file=sys.stderr)
        sys.exit(5)

    infer_time = time.time() - start_time
    print(f"[BiRefNet] Inference completed in {infer_time:.2f}s")

    # Create transparent output
    try:
        # Convert original image to RGBA
        output_image = image.convert('RGBA')
        
        # Create alpha mask from prediction
        alpha_mask = Image.fromarray(mask).convert('L')
        
        # Apply mask as alpha channel
        output_image.putalpha(alpha_mask)
        
        # Save output
        os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
        output_image.save(args.output, 'PNG')
        print(f"[BiRefNet] Saved output: {args.output}")
        
    except Exception as e:
        print(f"ERROR: Failed to save output: {e}", file=sys.stderr)
        sys.exit(5)

    print(f"[BiRefNet] Total time: {load_time + infer_time:.2f}s")
    sys.exit(0)


if __name__ == '__main__':
    main()
