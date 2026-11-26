# Sticky note alarms

Click the clock between collapse and options. A new alarm defaults to the next minute in the current local date (tomorrow at midnight if necessary). Choose a date in the calendar and click the hour/minute and AM/PM selectors (12-hour format; 12 AM is midnight and 12 PM is noon). Set alarm saves exactly one future deadline per note. The clock dot and tooltip show an armed alarm. Reopen the clock to change it; Remove clears it explicitly and Cancel leaves it unchanged.

The selected local date and time are converted to UTC at confirmation. A later time-zone change does not change the original instant; the displayed local time may change. Nonexistent and ambiguous daylight-saving times are rejected. No recurring schedule, system wake, sound or external notification is configured.

BranchWatch checks alarms with the existing one-second sticky-note timer. When due, the note's own color pulses around its outline (1.8 seconds in each direction) until acknowledged. Click the title or non-button header area to dismiss; this consumes the click rather than toggling temporary editing. Dragging the header and other header buttons retain their existing functions. The note's configured transparency is unchanged. Collapsed notes pulse around the header; click-through bodies render their lower outline on the passive nonactivating window, with the owner outline clipped to the header. The pulse is independent of body opacity. The grip remains usable.

Due time and triggered state are saved with the existing per-note data. After sleep or restart an overdue alarm triggers on the first update; a triggered alarm remains latched even if the system clock goes backward. Dismissal clears the deadline and latch. Closed notes and notes on other desktops do not automatically open, activate or switch desktops; the tray menu labels due notes and reopening or visiting the assigned desktop shows the pulse. Deleting a note deletes its alarm. Save failures use the existing sticky-note error and retry mechanism; persistence depends on successful disk writes.

## Windows acceptance

- Create two notes with different colors and alarm times. Check today selected and digital hour/minute and AM/PM controls; check midnight and noon; set a future date, reject past time, cancel changes, reschedule and remove.
- Verify only the due note pulses, in its selected color. Change color during a pulse; collapse and expand; change transparency and test level 1 and level 10. Verify click-through body and resize grip still work. Confirm no forced focus or desktop switch.
- Click the due title or blank header once. Verify the glow and dot disappear and do not return on later ticks or restart. Verify the next header click restores its ordinary temporary-edit behavior. Dragging must not acknowledge. Check clock and other controls remain usable.
- Restart before and after a deadline, sleep across a deadline, switch desktops, close/reopen a note and inspect the tray's due indicator. Check independently scheduled notes, saved titles and no recurring alarms.
- Test at minimum width, mixed DPI and different palettes. Automated tests exercise state, time validation, persistence and WPF properties; physical timing, popup layout, animation and native click-through need interactive Windows acceptance.
