# Third-party software and data notices

The top-level Apache License 2.0 applies to original STAGE source code, scripts, documentation, and author-produced artifact metadata unless a file says otherwise. It does not relicense third-party software, datasets, models, or publications.

## Build-time dependencies

The default workflow downloads these dependencies rather than vendoring their source:

- System.Text.Json 8.0.5 through NuGet.
- NumPy, Matplotlib, PyYAML, jsonschema, and Pillow at the versions pinned in <code>requirements.txt</code>.
- Optional PyTorch and torchvision versions listed in <code>requirements-mnist.txt</code>.

Each dependency remains under the license distributed by its upstream project and package. The corresponding package metadata and license files are authoritative.

## External comparison tools

BookSim2, SCALE-Sim, Timeloop, Accelergy, ZigZag, Unity, CUDA, and their source trees or binaries are not redistributed in this artifact. Configuration plans, invocation adapters, hashes, and normalized measurements produced for the paper are included. Reviewers who install those tools must comply with their upstream licenses.

## MNIST

The compressed MNIST archives are included only to make the frozen test-set and training provenance auditable. The artifact does not claim ownership of or relicense the MNIST dataset. The checkpoint, prediction table, and derived summaries are research outputs produced for this artifact. Users redistributing the dataset should verify the terms at the dataset's authoritative source.

## Literature-derived characterization

The component characterization catalog contains cited numerical values transcribed from publications. No third-party implementation source is included. Bibliographic attribution remains with the cited works.

Questions about a specific bundled file should be resolved conservatively: the upstream notice for that file or dataset takes precedence over the top-level STAGE license.
