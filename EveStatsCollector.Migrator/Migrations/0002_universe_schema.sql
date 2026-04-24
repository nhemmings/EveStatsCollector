CREATE TABLE regions (
    id   INTEGER PRIMARY KEY,
    name TEXT    NOT NULL
);

CREATE TABLE constellations (
    id        INTEGER PRIMARY KEY,
    name      TEXT    NOT NULL,
    region_id INTEGER NOT NULL REFERENCES regions(id)
);

CREATE TABLE solar_systems (
    id               INTEGER PRIMARY KEY,
    name             TEXT    NOT NULL,
    constellation_id INTEGER NOT NULL REFERENCES constellations(id),
    security_status  REAL    NOT NULL
);
