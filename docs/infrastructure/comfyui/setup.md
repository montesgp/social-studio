# Social Studio - ComfyUI Setup Guide

**Version:** 1.0\
**Audience:** Social Studio developers

## Purpose

This document describes the standard procedure for preparing a local
ComfyUI environment compatible with Social Studio image generation workflows.

------------------------------------------------------------------------

# Architecture

``` text
Social Studio
  │
  ├── Generates Workflow JSON
  │
  ▼
ComfyUI REST API (/prompt)
  │
  ▼
Checkpoint
  │
  ▼
IPAdapter
  │
  ▼
Image Generation
```

Social Studio does **not** execute ComfyUI workflows stored on disk. It generates
workflow JSON dynamically and submits it through the ComfyUI REST API.

------------------------------------------------------------------------

# Local Development

## Minimum Hardware

### AMD

-   RX580 8 GB (DirectML)
-   16 GB RAM
-   SSD

### NVIDIA

-   RTX 3060 12 GB
-   CUDA 12+
-   32 GB RAM

------------------------------------------------------------------------

# Required Software

-   Git
-   ComfyUI Portable
-   ComfyUI Manager
-   ComfyUI_IPAdapter_plus

## Optional: Detailer Preflight (face/hand repair)

Social Studio probes every ComfyUI server for `FaceDetailer` and `UltralyticsDetectorProvider`
before each generation (`docs/architecture/adr/0017-comfyui-detailer-preflight-probe.md`).
When **both** node classes are registered, Social Studio automatically submits a
detailer-augmented workflow that runs a face/hand repair pass after the primary sampler.
When either is absent, missing, or the probe cannot determine support, Social Studio falls back to
the plain workflow — silently and byte-identically to a server without these packs. No
configuration is required to benefit from this once both packs are installed; a per-org
`detailer_enabled=false` credential value force-disables it regardless of server support.

-   Impact-Pack (provides `FaceDetailer`)
-   Impact-Subpack (provides `UltralyticsDetectorProvider`)
-   Detector model, e.g. `bbox/face_yolov8m.pt`, in `models/ultralytics` (or the pack's
    expected folder)

------------------------------------------------------------------------

# Directory Layout

``` text
ComfyUI/
├── custom_nodes/
│   ├── ComfyUI-Manager/
│   └── ComfyUI_IPAdapter_plus/
│
└── models/
    ├── checkpoints/
    ├── clip_vision/
    ├── ipadapter/
    ├── loras/
    ├── vae/
    └── controlnet/
```

------------------------------------------------------------------------

# Installation

## 1. Install ComfyUI

Download ComfyUI Portable and verify:

-   http://127.0.0.1:8188

------------------------------------------------------------------------

## 2. Install Custom Nodes

``` bash
cd ComfyUI/custom_nodes

git clone https://github.com/ltdrdata/ComfyUI-Manager.git
git clone https://github.com/cubiq/ComfyUI_IPAdapter_plus.git
```

Restart ComfyUI.

------------------------------------------------------------------------

## 3. Install Models

### Checkpoint

Folder:

``` text
models/checkpoints
```

Recommended:

``` text
realisticVisionV60B1_v51HyperVAE.safetensors
```

------------------------------------------------------------------------

### CLIP Vision

Folder:

``` text
models/clip_vision
```

Required:

``` text
CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors
```

------------------------------------------------------------------------

### IPAdapter

Folder:

``` text
models/ipadapter
```

Required:

``` text
ip-adapter_sd15.safetensors
ip-adapter-plus_sd15.safetensors
ip-adapter-plus-face_sd15.safetensors
ip-adapter-full-face_sd15.safetensors
```

------------------------------------------------------------------------

# Verification Checklist

The following nodes must exist:

-   CheckpointLoaderSimple
-   CLIPTextEncode
-   EmptyLatentImage
-   KSampler
-   VAEDecode
-   SaveImage
-   IPAdapterUnifiedLoader
-   IPAdapterAdvanced

Optional (enables the detailer preflight, see above — Social Studio falls back to plain when
absent, no error):

-   FaceDetailer
-   UltralyticsDetectorProvider

The following API endpoints must respond:

-   GET /object_info
-   GET /system_stats
-   GET /queue
-   GET /history
-   POST /prompt

------------------------------------------------------------------------

# Production Recommendations

Recommended GPUs:

  Tier         GPU
  ------------ -------------------
  Entry        RTX 4070 Ti Super
  Standard     RTX 4090
  Cloud        NVIDIA L40S
  Enterprise   NVIDIA A100

Deploy ComfyUI in Docker and mount models as external volumes.

``` text
/models
/checkpoints
/ipadapter
/clip_vision
/loras
/controlnet
```

------------------------------------------------------------------------

# Automation Roadmap

## hive setup comfy

Responsibilities:

-   Detect GPU
-   Detect CUDA/DirectML
-   Download ComfyUI
-   Install required custom nodes
-   Download required models
-   Generate Social Studio configuration

------------------------------------------------------------------------

## hive doctor

Validation:

-   ComfyUI reachable
-   Compatible version
-   Required nodes installed
-   Required models installed
-   API responding
-   Workflow compatibility

Example:

``` text
Social Studio Environment Check

✓ ComfyUI
✓ Checkpoint
✓ IPAdapter
✓ CLIP Vision
✓ API

READY TO GENERATE
```

------------------------------------------------------------------------

# Design Principles

-   Social Studio should never hardcode model filenames.
-   Social Studio should discover installed nodes through `/object_info`.
-   Social Studio should maintain a model registry abstraction.
-   All environment validation should be automated.
