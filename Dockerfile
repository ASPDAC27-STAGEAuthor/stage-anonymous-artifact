FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-venv python3-pip \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /artifact
COPY . /artifact
RUN python3 -m venv /artifact/.venv \
    && /artifact/.venv/bin/python -m pip install --no-cache-dir --upgrade pip \
    && /artifact/.venv/bin/python -m pip install --no-cache-dir -r requirements.txt

ENV STAGE_SKIP_BOOTSTRAP=1
CMD ["bash", "scripts/run_all.sh", "--skip-bootstrap"]
