CREATE TABLE kills_reports (
    id            SERIAL      PRIMARY KEY,
    last_modified TIMESTAMPTZ NOT NULL,
    fetched_at    TIMESTAMPTZ NOT NULL
);

CREATE TABLE kills_entries (
    id         BIGSERIAL PRIMARY KEY,
    report_id  INT       NOT NULL REFERENCES kills_reports(id),
    system_id  INT       NOT NULL,
    ship_kills INT       NOT NULL,
    npc_kills  INT       NOT NULL,
    pod_kills  INT       NOT NULL
);

CREATE INDEX idx_kills_entries_report_id ON kills_entries(report_id);
CREATE INDEX idx_kills_entries_system_id ON kills_entries(system_id);

CREATE TABLE jumps_reports (
    id            SERIAL      PRIMARY KEY,
    last_modified TIMESTAMPTZ NOT NULL,
    fetched_at    TIMESTAMPTZ NOT NULL
);

CREATE TABLE jumps_entries (
    id         BIGSERIAL PRIMARY KEY,
    report_id  INT       NOT NULL REFERENCES jumps_reports(id),
    system_id  INT       NOT NULL,
    ship_jumps INT       NOT NULL
);

CREATE INDEX idx_jumps_entries_report_id ON jumps_entries(report_id);
CREATE INDEX idx_jumps_entries_system_id ON jumps_entries(system_id);
